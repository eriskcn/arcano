# Ponytail Audit: Arcano whole repo

Date: 2026-10-30 23:23
Scope: whole repo (backend, frontend, scripts, Compose). Read-only review; no code changed.

## What this repo does

Arcano is a personal cloud-storage app. Users sign up, then upload, list, download, and delete their own files. The API stores file details in PostgreSQL and file bytes in SeaweedFS, an S3-compatible store. The front end is Angular.

**Load assumed:** one API copy, one database, and a small number of users. Sign-up is open, so anyone can reach the upload endpoint.

## Must fix

1. **The dev signing key for login tokens is committed to git** (`backend/src/ArcanoCore.API/appsettings.Development.json:19`)
   - **What this is:** The dev settings file holds the key that signs login tokens.
   - **Problem:** Git tracks this file, and the key is in the history since the first commit. The `.gitignore` line added this session does not hide a file git already tracks. Anyone with the repo can forge tokens for any API instance that uses this key.
   - **Fix:** Run `git rm --cached` on that file. Move `Jwt:Secret` to user secrets or the `Jwt__Secret` environment variable. Rotate the key.
   - **If we skip it:** Anyone with repo access can sign in as any user on any instance that uses this key.

2. **The SeaweedFS signing key is not ignored by git** (`backend/seaweedfs/security.toml`)
   - **What this is:** The file holds the signing key for SeaweedFS's S3 login. It's for dev only.
   - **Problem:** It's untracked and matches no ignore rule. `s3.json` is ignored, but this file isn't. A `git add .` would commit it.
   - **Fix:** Add `seaweedfs/security.toml` to `backend/.gitignore`.
   - **If we skip it:** The key lands in the next commit.

3. **Delete removes the file before the database row changes** (`backend/src/ArcanoCore.Application/Services/FileService.cs:137-142`)
   - **What this is:** Delete removes the file from storage, then marks the database row as deleted.
   - **Problem:** If the database save fails, the row still says the file exists, but its content is gone. Scenario: the database drops during a delete. The file stays in the list, and every download fails.
   - **Fix:** Swap the two steps. Set `IsDeleted` and save first, then call `DeleteAsync`. If the storage delete fails afterward, the leftover object is harmless.
   - **If we skip it:** Files become unreadable after a failed delete, and the list still shows them.

4. **Anyone can fill the server disk** (`backend/src/ArcanoCore.API/Controllers/FilesController.cs:23`)
   - **What this is:** `[DisableRequestSizeLimit]` turns off the size cap on uploads.
   - **Problem:** Sign-up is open, so anyone can register and upload a file of any size. Scenario: one user uploads a 50 GB file, and storage fills up for everyone.
   - **Fix:** Replace it with `[RequestSizeLimit(n)]`, where `n` is a size you choose, such as 100 MB. This is a product decision.
   - **If we skip it:** One upload can stop storage for all users.

5. **Internal error text is sent to the browser** (`backend/src/ArcanoCore.API/Middleware/ExceptionHandlingMiddleware.cs:43`, `FileService.cs:77, 118, 148`)
   - **What this is:** When something fails, the API returns the raw exception message to the caller.
   - **Problem:** Storage errors can name internal hosts and buckets, such as `seaweedfs:8333`. Every unhandled error goes to the browser this way.
   - **Fix:** Return a fixed message, such as "Storage is unavailable." The middleware already logs the real exception on the server.
   - **If we skip it:** Attackers learn your internal network layout from the error text.

## Should fix

6. **A failed upload leaves the file in storage forever** (`FileService.cs:48-61`)
   - **What this is:** Upload writes the file to storage first, then saves the database row.
   - **Problem:** If the database save fails, the file stays in storage with no row pointing to it, and nothing cleans it up.
   - **Fix:** In the `catch` block, delete the stored object before returning the error.
   - **If we skip it:** Storage fills with invisible files you still pay for.

7. **The delete-failure test does not test delete failure** (`backend/test/ArcanoCore.Domain.UnitTests/Services/FileServiceTests.cs:60-72`)
   - **What this is:** The test named "delete failure when DB save fails" expects `File.NotFound`.
   - **Problem:** It passes because the query hides the deleted row, so the save never runs. A bug in the save path would not fail this test.
   - **Fix:** Make the row visible to the query, make `SaveChangesAsync` throw, and assert the `DeleteFailed` result. Do this after fix 3, so the test checks the corrected order.
   - **If we skip it:** The bug in fix 3 can come back without any test failing.

8. **Sign-in and sign-up have no tests** (`backend/src/ArcanoCore.API/Controllers/AuthController.cs`)
   - **What this is:** The sign-up and sign-in code, including the password check.
   - **Problem:** The test project covers only the domain layer. A broken password check would ship unnoticed.
   - **Fix:** Add two tests: the correct password returns a token, and a wrong password returns 401.
   - **If we skip it:** Auth bugs reach users silently.

9. **Sign-up and sign-in reveal which emails exist** (`AuthController.cs:56, 99-115`)
   - **What this is:** Sign-up says "Email is already in use." Sign-in takes a different amount of time depending on whether the email exists.
   - **Problem:** Someone can test a list of emails and learn which ones have accounts. When the filter says no, sign-in returns at once. When the email exists, it runs the slower password check.
   - **Fix:** Decide whether this matters to you. The cheapest fix is one generic message on sign-up. Removing the timing gap needs a dummy password check on the fast path.
   - **If we skip it:** Anyone can build a list of your users' emails.

10. **The bloom filters break with two API copies** (`FileBloomFilter.cs`, `EmailBloomFilter.cs`)
    - **What this is:** Each API process keeps its own in-memory filter of known file IDs and emails.
    - **Problem:** With two copies, a file uploaded on copy A returns "not found" on copy B, and a user who signed up on A can't sign in on B. One copy is fine, which is the load assumed.
    - **Fix:** Remove the filters (item 14), so each check goes to the database.
    - **If we skip it:** Adding a second copy breaks downloads and sign-ins at random.

11. **Upload returns a link the browser can't open** (`SeaweedStorageService.cs:77-85`, `FileService.cs:65`)
    - **What this is:** Upload returns a signed download URL.
    - **Problem:** The URL uses `http://seaweedfs:8333`, a Docker-internal name the browser can't reach. The front end never reads it, and creating it costs one extra call per upload.
    - **Fix:** Remove it: `GetUrlAsync` from the interface and implementation, plus `FileResponseDto.Url`.
    - **If we skip it:** Every upload reply carries a broken link, and the dead code stays.

12. **The file list returns every file at once** (`FileService.cs`, `ListFilesAsync`)
    - **What this is:** Listing returns all of a user's files in one reply.
    - **Problem:** A user with thousands of files gets a huge reply on every page load. Fine at today's size.
    - **Fix:** Add `Skip`/`Take` with a page size of about 50, in both the query and the controller.
    - **If we skip it:** The list gets slow as files grow.

13. **The dev web server listens on your whole network** (`run-dev.sh:64`)
    - **What this is:** `npm start -- --host 0.0.0.0` makes the front-end dev server listen on every network interface.
    - **Problem:** Anyone on your Wi-Fi can open it. Low risk, but unnecessary.
    - **Fix:** Remove `-- --host 0.0.0.0`.
    - **If we skip it:** Your dev build is visible to the local network.

14. **Delete both bloom filters** (`BloomFilter.cs`, `FileBloomFilter.cs`, `EmailBloomFilter.cs`, `IBloomFilter.cs`, and their tests)
    - **What this is:** Two in-memory lists that answer "might this ID exist?" before the real database check.
    - **Problem:** The database already answers this through indexed key and email lookups. The filters add about 250 lines, a full table scan on first use, and the two-copy bug (item 10). The WBS does not require them.
    - **Fix:** Remove the filter classes and their DI registrations. Use the database check.
    - **If we skip it:** You keep about 250 lines and a bug to maintain.

15. **Download and Delete repeat the same lookup** (`FileService.cs`, `DownloadFileAsync` and `DeleteFileAsync`)
    - **What this is:** Both methods find the file, then check the owner.
    - **Problem:** The same code appears twice, so a fix must be made in two places.
    - **Fix:** Move it to one private helper, `FindOwnedFileAsync`. Saves about 10 lines.
    - **If we skip it:** The two copies can drift apart.

16. **The owner-ID parse is written three times** (`FileService.cs:41, 83, 153`)
    - **What this is:** Each method parses `_currentUser.UserId` as a `Guid` itself.
    - **Fix:** Parse it once in a single property. Saves about 8 lines.

## Nice to have

17. **The bucket check runs on every upload** (`SeaweedStorageService.cs:42`). That is one extra storage call per upload. Create the bucket once at startup.
18. **The backend README is out of date** (`backend/README.md:60`). It says the SeaweedFS service has no S3 identity file. It does now. Update that line.
19. **The JWT secret is checked twice** (`DependencyInjection.cs`, `AuthController.cs` `GenerateJwtToken`). Check it once at startup.

## Verdict

Fix items 1 and 2 first, because they expose secrets in the repo. Then items 3 and 4, and 5. Items 1, 2, and 5 are small. Items 3 and 4 need a short decision on the size limit.

**Lean:** `-250 lines`, `-0 dependencies possible`, from deleting the bloom filters and merging the duplicated code.

## Not checked

- The Angular app code beyond the token handling and file client. `app.ts` was not read in full.
- The Dockerfile, the EF migration, and the Compose setup beyond the points above.
- Token expiry and sign-out behavior.
- The tests and the app were not run, and load and timing were not measured. All findings come from reading the code.

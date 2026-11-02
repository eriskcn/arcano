# Arcano User Manual

Arcano stores your files on a private server and lets you open them in a browser. This manual explains how to use it. The name shown in the app is set by the server, and is **ArcanoDrive** by default.

## Contents

1. [Getting started](#getting-started)
2. [Finding your way around](#finding-your-way-around)
3. [Files and folders](#files-and-folders)
4. [Previews](#previews)
5. [Trash](#trash)
6. [Sharing](#sharing)
7. [Storage](#storage)
8. [Account settings](#account-settings)
9. [Keyboard shortcuts](#keyboard-shortcuts)
10. [Admin](#admin)
11. [Troubleshooting](#troubleshooting)

## Getting started

### Create the first account

The first account on a new server needs no key and becomes the admin.

1. Open the app and select **Sign up**.
2. Check that the page says "This server has no accounts yet. The first account becomes the admin."
3. Enter your **Email address** and a **Password** of at least 8 characters.
4. Select **Create account**.
5. The sign-in page opens and says "Account created. Sign in to continue." Sign in with the same details.

### Sign up with an invite key

Every later account needs an invite key from an admin. A key works once.

1. Open the app and select **Sign up**.
2. Enter your **Email address** and a **Password** of at least 8 characters.
3. Paste the key into **Invite key**.
4. Select **Create account**.
5. The sign-in page opens and says "Account created. Sign in to continue." Sign in with the same details.

If sign-up fails because of your email or password, the key is not used up. You can try again with it.

### Sign in

1. Enter your **Email address** and **Password**.
2. Select **Sign in**.

### Sign out

Select the **Sign out** icon at the right end of the top bar. This signs you out of this browser only. To end all sessions, see [Sign out everywhere](#sign-out-everywhere).

### How long you stay signed in

You stay signed in on this browser, even if you close it, until your session ends. The session renews itself while you use the app. By default, you may need to sign in again after about an hour without use. You always need to sign in again about 24 hours after your last sign-in.

### Forgotten password

The app does not send reset emails. An admin gives you a Reset key instead.

1. Ask an admin for a Reset key.
2. On the sign-in page, select **Have a reset key?**. You can also go to `/reset` on your server.
3. Enter the **Reset key**.
4. Enter your new password twice. It must be at least 8 characters.
5. Select **Reset password**.
6. The sign-in page opens and says "Password reset. Sign in with your new password." Sign in with the new password.

A Reset key works once. Using it signs you out of all your sessions.

## Finding your way around

The sections are:

| Section | What it shows |
|---|---|
| **Home** | Your files, most recently changed first. This is the same list as Recent. |
| **My Files** | Your files and folders. The app opens here. |
| **Recent** | Your files, most recently changed first. Folders are not listed. |
| **Favorites** | The files you have starred. |
| **Trash** | Files you have deleted. |
| **Shared with me** | Items other users have shared with you. |

The layout depends on the width of your screen:

- **Large screens (1024 pixels wide and up):** the sections are in the left sidebar. **Settings** and **Admin** are at the bottom of it.
- **Smaller screens, including phones:** the first five sections are in the bottom bar. Select the menu icon at the top left to reach **Shared with me**, **Settings**, **Admin** and your storage meter. If the menu stays in front of a dialog you opened from it, tap outside the menu to close it.
- **Wider screens (1280 pixels wide and up):** selecting an item also opens a **Details** panel on the right. It has quick actions such as **Preview**, **Download** and **Share**. Narrower screens do not show this panel, so use the [right-click menu](#the-right-click-menu).

Other controls:

- Select **List view** or **Grid view** at the top to change how items are shown. These are hidden on phones.
- Select **All**, **Folders**, **Documents**, **Images** or **Videos** to filter the items that have loaded.
- Scroll down to load more. Files load 100 at a time.

## Files and folders

### Upload files

1. Open the folder you want to upload into.
2. Select **Upload**.
3. Choose one or more files.

Files upload one at a time. A bar shows the file name, its progress, and a count such as "2 of 5". Most other actions must wait until the upload ends.

If the upload starts from **Home**, **Recent**, **Favorites** or **Trash**, the files go to the top level of My Files.

### Upload from a phone

1. Tap the round upload button at the bottom right.
2. Choose your files from the phone's file picker.

### Large files

Files over 8 MB are sent in pieces of 8 MB. Each failed piece is retried up to three times. The bar moves forward as each piece finishes, and stays at 99% until the server finishes the file.

Keep the page open until the upload finishes. An upload cannot resume after the page closes or the upload fails. You must start it again. The app has no button to cancel an upload.

The largest possible file is about 78 GB, but your storage limit usually applies first.

### Upload by drag and drop

1. Drag files from your computer onto the file list. They upload to the folder you have open.
2. To upload into a subfolder instead, drop them on that folder's row or card.

Drag and drop only works when the list shows at least one item. In an empty folder, use **Upload**.

### Create a folder

1. Select **New**. On a phone it shows only a plus sign.
2. Type a **Name**.
3. Press Enter or select **Save**. Press Esc or select **Cancel** to stop.

Two folders in the same place cannot have the same name.

### Open a folder

Double-click it, or select it and press Enter. Use the path at the top to go back up. Selecting **My Files** in the sidebar while you are already in My Files does not take you back to the top.

Phones do not show the path. To return to the top level on a phone, tap another section and then **My Files**.

### Download a file

1. Right-click the file.
2. Select **Download**.

The browser saves the file only after all of it has been fetched. There is no progress bar, so a large file can take a while.

### Download a folder as a zip

1. Right-click the folder.
2. Select **Download as zip**.

The zip keeps the subfolders. It cannot hold more than 5,000 files. Download works on one item at a time, so you cannot download a multi-selection.

### Rename

1. Right-click the file or folder and select **Rename**, or select it and press F2.
2. Type the new **Name**.
3. Press Enter or select **Save**.

Names can be up to 255 characters.

### Move

Use any of these:

- **Menu:** right-click an item, select **Move to…**, then choose a target. The list only shows folders inside the current folder, plus **My Files** if you are in a subfolder.
- **Drag:** drag an item onto a folder, or onto a name in the path at the top.
- **Cut and paste (files only):** select **Cut** or press Ctrl+X. Go to the target folder in My Files, then press Ctrl+V or right-click empty space and select **Paste**.

Cut and paste can reach any folder. Use it when the target is not in the **Move to…** list.

### Copy

Only files can be copied. Folders cannot.

- **Copy in the same place:** right-click a file and select **Make a copy**.
- **Copy to another folder:** select **Copy** or press Ctrl+C. Go to the target folder in My Files, then press Ctrl+V or right-click empty space and select **Paste**.

Each new file is named "Copy of" plus the original name. Copies count toward your storage.

### Star a file or folder

1. Right-click the item.
2. Select **Add to favorites**. To undo this, select **Remove from favorites**.

A star appears next to the name. The **Favorites** section lists starred files only. Starred folders keep their star in My Files but are not listed in Favorites.

### Search

Type in the **Search files and folders…** box at the top.

- Search looks at file names only. Folders do not appear in results.
- Upper and lower case do not matter.
- It searches all your files, not just the folder you have open.
- It does not search the Trash or **Shared with me**.
- In **Favorites**, it only shows starred files.

Clear the box to return to the normal list.

### Select several items

Use any of these:

- Hold Ctrl (Cmd on a Mac) and click an item to add it to the selection. Do the same on a selected item to remove it.
- Click one item, then hold Shift and click another to select both and everything between them. Hold Ctrl+Shift (Cmd+Shift on a Mac) instead, to add them to what is already selected.
- Press and drag from empty space in the list to draw a box around items. Hold Ctrl (Cmd on a Mac) as you start, to add to what is already selected.
- Press Ctrl+A (Cmd+A on a Mac) to select everything in the list.

The count of selected items appears under the section title. Press Esc to clear the selection.

Some actions apply only to the files in a selection and skip any folders: **Copy**, **Cut**, **Move to trash** and the Delete key. When folders are skipped, the app shows "Folders were skipped. Act on them one at a time." **Move to…** and the favorites action apply to files and folders.

### The right-click menu

Right-click an item, or select the **⋮** button that appears when you point at it. On a touch screen, try pressing and holding the item. If that does nothing, tap the far right of the row, or the top right corner of the card in grid view. The **⋮** button is there even when you cannot see it.

| You right-click | Menu options |
|---|---|
| One file | **Preview**, **Download**, **Share**, **Rename**, **Make a copy**, **Copy**, **Cut**, **Move to…**, **Add to favorites**, **Show details**, **Move to trash** |
| One folder | **Open**, **Download as zip**, **Share**, **Rename**, **Move to…**, **Add to favorites**, **Show details**, **Move to trash** |
| Several selected items | **Copy**, **Cut**, **Move to…**, **Add to favorites**, **Move to trash** |
| Empty space | **New folder**, **Upload files**, **Paste** |
| A file in the Trash | **Restore**, **Delete forever** |

## Previews

To preview a file, double-click it, or right-click it and select **Preview**.

| File type | Preview |
|---|---|
| PNG, JPEG, GIF, WebP and BMP images | Shown in the app |
| Audio and video | Played in the app, if your browser supports the format |
| Text files and JSON | Shows the first 100 KB. Download the file to see the rest. |
| HTML, SVG and XML | Download only. These are blocked for safety. |
| PDF, Word, Excel, zip and all other types | Download only |
| Any file over 25 MB | Download only |

The app relies on the file type your browser reported at upload, not only the file extension. A file with an unusual extension may be download only.

Double-clicking a file that cannot be previewed does nothing. Selecting **Preview** on one shows "Preview unavailable." with a **Download** button.

## Trash

### Delete a file

Right-click the file and select **Move to trash**, or select it and press Delete. To bring it back straight away, press Ctrl+Z.

### Delete a folder

1. Right-click the folder and select **Move to trash**, or select only that folder and press Delete.
2. Select **Delete folder** to confirm.

The folder and its subfolders are removed, and all their files go to the Trash. The folders do not appear in the Trash and cannot be restored. Only the files can.

### Restore a file

1. Open **Trash**.
2. Right-click the file.
3. Select **Restore**.

The file returns to its original folder. If that folder was deleted, the file goes to the top level of My Files.

### Delete a file forever

1. Open **Trash**.
2. Right-click the file and select **Delete forever**. To do this for several selected files, press Shift+Delete.
3. Select **Delete forever** to confirm.

This cannot be undone.

### Empty the Trash

1. Open **Trash**.
2. Select **Empty trash**.
3. Select **Empty trash** again to confirm.

This cannot be undone.

### Automatic removal

Files are permanently deleted 30 days after you move them to the Trash. The server checks for these regularly, so removal can happen a little after the 30 days are up.

Files in the Trash still count toward your storage. To free the space, delete them forever.

## Sharing

There are two ways to share. Both are in the **Share** dialog.

| | Public link | Share with a user |
|---|---|---|
| **What you can share** | Files only | Files and folders |
| **Who can open it** | Anyone who has the link | One user, by account email |
| **Account needed** | No | Yes, they must have an account on this server |
| **Where they find it** | They open the link you send them | In their **Shared with me** section |
| **Expiry** | Never, 1 day, 7 days or 30 days | Never expires |
| **How to stop it** | **Revoke** | **Remove** |

Both are read-only. Nobody you share with can change your files.

The app does not send any notification. Tell the other person yourself.

### Create a public link

1. Right-click a file and select **Share**.
2. Under **Link expires**, choose **Never**, **1 day**, **7 days** or **30 days**.
3. Select **Create link**.
4. Select **Copy** next to the new link and send it to the other person. **Copy** gives no confirmation on screen.

A file can have several links at once.

### What a link recipient sees

The recipient does not need to sign in. They see the file name, its size and a **Download** button. They also see a preview if the file type supports one. See [Previews](#previews).

An expired or revoked link shows "This link is invalid or has expired."

### Revoke a public link

1. Right-click the file and select **Share**.
2. Select **Revoke** next to the link.

The link stops working at once. There is no confirmation step.

If you move a file to the Trash, its links stop working. If you restore the file, links that have not expired or been revoked work again.

### Share with another user

1. Right-click a file or folder and select **Share**.
2. Under **People**, type the person's email address.
3. Select **Share**.

They must have an account on this server and it must not be disabled. You cannot share with yourself.

The **Details** panel always shows "Only you" next to **Shared**, even for shared items. To see who has access, open the **Share** dialog.

### What a user recipient can and cannot do

A recipient can:

- See the item in **Shared with me**, with your email address next to it.
- Open a shared folder and its subfolders.
- Preview and download files.
- Download a shared folder or subfolder as a zip.
- Remove the item from their own list.

A recipient cannot:

- Upload, rename, move, delete or star anything in it.
- Share it with anyone else.

For a shared folder, they always see its current contents, including anything you add later. Shared items do not use the recipient's storage.

A shared item disappears from the recipient's list if you move it to the Trash or your account is disabled or deleted.

### Use the "Shared with me" section

1. Open **Shared with me**.
2. For a file, select **Preview** or **Download**.
3. For a folder, select **Open** or **Download zip**.
4. Inside a folder, select a subfolder's name to open it. Use the path at the top to go back. Select **Load more** if it appears.

### Stop sharing with a user

If you are the owner:

1. Right-click the item and select **Share**.
2. Select **Remove** next to the person's email.

Access ends at once. There is no confirmation step.

If you are the recipient:

1. In **Shared with me**, select **Remove** next to the item.
2. Select **Remove** to confirm.

The owner keeps the original.

## Storage

The storage meter shows how much you have used, for example "1.2 GB of 5 GB used". It is at the bottom of the sidebar. On smaller screens it is in the menu at the top left. Collapsing the sidebar hides it.

- The default limit is 5 GB per person. The server can set a different default, and an admin can set a different limit for you.
- The meter shows a percentage and turns amber at 80%. It turns red at 95%.
- Files in the Trash count toward your usage.
- Items shared with you do not count toward your usage.
- If you do not have enough space, an upload or copy fails with "Not enough storage space."

To free space, delete files and then remove them from the [Trash](#trash).

## Account settings

Select **Settings** to open these options. On smaller screens it is in the menu at the top left.

### Change your password

1. Enter your **Current password**.
2. Enter your new password twice. It must be at least 8 characters.
3. Select **Change password**.

You stay signed in here. Your other sessions are signed out.

### Sign out everywhere

Under **Sessions**, select **Sign out everywhere**. This ends all your sessions on every device, including this one.

### Delete your account

1. Select **Delete account**.
2. Read the warning and enter your password.
3. Select **Delete everything permanently**.

This permanently deletes your account and all your files, folders and shared links, including the Trash. It cannot be undone.

If you are the last active admin and other accounts exist, you cannot delete your account. See the [last-admin rule](#the-last-admin-rule).

## Keyboard shortcuts

Shortcuts do not work while you type in a field or while a dialog is open. On a Mac, use Cmd in place of Ctrl.

| Keys | Action | Notes |
|---|---|---|
| Ctrl+A | Select everything in the list | |
| Enter | Open the selected folder | One folder selected. Does nothing if a button or field has focus. |
| F2 | Rename | One item selected. Not in Trash. |
| Delete | Move selected files to Trash | Skips folders, except one folder selected alone, which asks you to confirm deleting it. Not in Trash. |
| Shift+Delete | Delete selected files forever | In Trash only. Asks you to confirm. |
| Ctrl+C | Copy selected files | Skips folders and says so. Not in Trash. |
| Ctrl+X | Cut selected files | Skips folders and says so. Not in Trash. |
| Ctrl+V | Paste files | In My Files only. |
| Ctrl+Z | Undo | See below. |
| Esc | Close the open menu or dialog, or clear the selection | |
| Up and Down arrows | Move through the open menu | |

Ctrl+Z undoes these actions: a move, a paste, **Make a copy**, and moving files to the Trash with the Delete key or the menu. It remembers the last 20. It does not undo deleting a folder, deleting from the **Details** panel, or deleting forever. Reloading the page clears what it remembers.

## Admin

This section is for admins. Select **Admin** to open the dialog. On smaller screens it is in the menu at the top left. Other users do not see this option.

Admins can see each member's email and storage use. There is no way for an admin to open another member's files.

The app has no button to make another member an admin.

### Invite someone

1. Under **Invite keys**, optionally enter a **Quota (GB)** for the new account. Leave it empty to use the server default.
2. Optionally enter **Expires in (days)**, from 1 to 30. Leave it empty for 7 days.
3. Select **Generate join key**.
4. Select **Copy**.
5. Send the key to the person yourself. The app does not send it.

### Keys are shown once

A new key appears only once, in the box that says "Copy it now: it cannot be shown again." It is gone once you close the dialog or create another key. If you lose a key, revoke it and create a new one.

Each key works once.

### Reset a member's password

1. Under **Members**, find the person and select **Reset key**.
2. Find the new key in the box under **Invite keys**, and select **Copy**.
3. Send the key to the person yourself.
4. Tell them to follow the steps in [Forgotten password](#forgotten-password).

A Reset key expires after 7 days. You cannot change this.

### Revoke a key

Unused keys that have not expired are listed under **Invite keys**. Select **Revoke** next to a key. It stops working at once. There is no confirmation step.

### Set a member's storage limit

1. Under **Members**, find the person and select **Edit quota**.
2. Enter a limit in GB. Leave it empty to use the server default.
3. Select **Save**.

Setting a limit below what a person already uses deletes nothing. They just cannot add more until they are under the limit. A limit of 0 is allowed.

### Disable or enable a member

Under **Members**, select **Disable** or **Enable**.

Disabling a member:

- Signs them out at once and stops them signing in.
- Keeps their files.
- Hides what they shared with other users until they are enabled again.
- Stops their public links while they are disabled. The same links work again when the member is enabled, unless they have expired.

### Delete a member

1. Under **Members**, select **Delete**.
2. Select **Delete** to confirm.

This permanently deletes the person and all their files. It cannot be undone.

### The last-admin rule

The last active admin cannot be disabled or deleted. There is one exception: if you are the only account on the server, you can delete your own account from **Settings**. The server then treats the next sign-up as the first account.

Be careful with your own row, which shows the warning "This is your account. Disabling or deleting it will lock you out." If you are not the last admin, you can still disable or delete yourself.

## Troubleshooting

### Sign-in and sign-up

| Message or problem | Meaning | What to do |
|---|---|---|
| Invalid email or password. | The details are wrong. This is also shown if the account is disabled or temporarily locked after repeated wrong passwords. | Check your details. If it keeps happening, wait a few minutes, then ask an admin. |
| Too many attempts. Wait a minute and try again. | More than 10 sign-in, sign-up or reset attempts came from your network in one minute. | Wait a minute. |
| Unable to connect to the server. Check your connection and try again. | The browser cannot reach the server. | Check your connection. |
| This invite key is invalid, expired or already used. | The key cannot be used. | Ask an admin for a new key. |
| This reset key is invalid, expired or already used. | The key cannot be used. | Ask an admin for a new key. |
| Could not create account. | Usually an account with that email already exists. | Sign in, or ask an admin for a Reset key. |
| The new password must be at least 8 characters. | The password is too short. | Use a longer password. |
| The new passwords do not match. | The two entries differ. | Type them again. |
| My email looks different from what I typed. | For Gmail addresses, dots and anything after a `+` are removed. For Outlook, Hotmail and Live addresses, anything after a `+` is removed. | Nothing. Both forms reach the same account. |
| I am the only admin and I forgot my password. | Only an admin can create a Reset key, so the app cannot help here. | Ask the person who runs the server. |

### Files

| Message or problem | Meaning | What to do |
|---|---|---|
| Unable to load your files. Please try again. | The request failed. Your session may have ended. | Select **Try again**. If that fails, reload the page. You will go to the sign-in page if your session ended. |
| Not enough storage space. | The file does not fit in your limit. | Free some space. See [Storage](#storage). |
| File is too large. | The server refused the upload because of its size. | Ask the person who runs the server. |
| Unable to upload (file name). Please try again. | The upload failed. Common causes are listed below. | Check the causes below, then try again. |
| Unable to download (name). Please try again. | The download failed. | Try again. |
| This folder has too many files to download as a zip. | A zip cannot hold more than 5,000 files. | Download smaller subfolders. |
| A folder with this name already exists here. | Folder names must be unique in one place. | Choose another name. |
| Enter a valid folder name. / Enter a valid file name. | The name is empty or over 255 characters. | Change the name. |
| A folder cannot be moved into itself or one of its subfolders. | The target is inside the folder you are moving. | Choose another target. |
| No folders to move to | The current folder has no subfolders, and you are at the top level. | Use drag and drop or cut and paste, or create a folder first. |
| Preview unavailable. | The file type or size has no preview, or loading failed. | Select **Download**. See [Previews](#previews). |
| The Sort, Filter, notifications and profile buttons do nothing. | These controls have no function yet. | Use the filter buttons under the title, such as **Images**. |

An upload can also fail for these reasons:

- The file is empty (0 bytes).
- The file name is over 255 characters.
- You already have 3 large uploads open. An upload stays open for up to 24 hours if you closed the page while it ran. It can also use up your available space during that time.

### Sharing

| Message or problem | Meaning | What to do |
|---|---|---|
| This item cannot be shared with that recipient. | The email has no account here, the account is disabled, or it is your own. | Check the email. |
| Unable to share. | One cause is over 20 share attempts in a minute. | Wait a minute and try again. |
| This link is invalid or has expired. | The link was revoked, has expired or was mistyped, or the file is in the Trash or deleted. It can also appear after too many requests from one network (over 60 a minute). | Wait a minute and try again. Then ask the owner for a new link. |
| Download failed. Please try again. | A public link download failed. Over 20 downloads a minute from one network will cause this, and each preview counts as one. | Wait a minute and try again. |
| Unable to open this folder. | Access may have ended, or the folder was removed. | Go back to **Shared with me** to refresh the list. |

### Account and admin

| Message | Meaning | What to do |
|---|---|---|
| Current password is incorrect. / Password is incorrect. | The password you entered is wrong. | Try again. |
| The last admin cannot be deleted or disabled. / The last active admin cannot be deleted or disabled. | See the [last-admin rule](#the-last-admin-rule). | Keep at least one active admin. |
| Enter a quota of 0 or more GB, or leave it empty. | The quota is not valid. | Fix the number. |
| Expiry must be a whole number of days from 1 to 30. | The expiry is not valid. | Fix the number. |
| Copy failed. Select the key and copy it manually. | The browser blocked copying. | Select the key and copy it yourself. |
| Something went wrong. Please try again. | An admin action failed. | Try again. If you just disabled or deleted your own account, reload the page. |

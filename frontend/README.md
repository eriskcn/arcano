# ArcanoClient

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.0.8.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

The client reads the API base URL from `src/environments/`:

- `environment.ts` (development): `http://localhost:5000/api`, matching the backend's local HTTP launch profile.
- `environment.production.ts` (used by the production build and the nginx image): the same-origin path `/api`.

Sign-in and registration use the backend `/Auth/login` and `/Auth/register` endpoints. The JWT is kept in tab-scoped session storage and attached only to requests for the configured API. The client renews the token with `POST Auth/refresh` and revokes it on sign-out with `POST Auth/logout`. Tokens carry a `role` claim; when it is `Admin` the client shows the Admin entry (the server still enforces access).

## Features

- File explorer with folder zip download (context menu and details panel).
- Folder star is persisted (the Favorites view still lists files only).
- On phones (under 640 px) a floating upload button opens the file picker.
- Sign-up asks for an invite key (hidden on a fresh server, where the first account becomes the admin); `/reset` takes a reset key and a new password.
- The first account on a fresh server is the admin.
- Admin dialog (admins only, from the sidebar): members with usage, quota edit, disable/enable, delete; generate join keys and member reset keys (shown once, with copy) and revoke open keys.
- The old "Shared" filter for public links stays removed; share links are created per file from the Share dialog.
- Share dialog People section: share a file or folder read-only with a registered user by email.
- "Shared with me" (sidebar and drawer, not the mobile bottom bar): browse, preview, download and zip what others shared; remove a share with a confirmation.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

`npm test` runs `ng test`, which executes the unit tests with the [Vitest](https://vitest.dev/) test runner:

```bash
npm test
```

There is no end-to-end test target in `angular.json`.

## Production image

`Dockerfile` builds the app (`npm run build`) and serves `dist/arcano-client/browser` with nginx, using `nginx.conf`. The production build calls the API at `/api` on the same origin, so the nginx configuration proxies `/api` to the backend service; see `../docs/DEPLOY.md` and `../docker-compose.prod.yml`.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

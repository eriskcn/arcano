declare const process: any;

export const environment = {
  appName: (typeof process !== 'undefined' && process?.env?.['APP_NAME']) || 'ArcanoDrive',
  apiBaseUrl: 'http://localhost:5000/api',
};

declare const process: any;

export const environment = {
  appName: (typeof process !== 'undefined' && process?.env?.['APP_NAME']) || 'ArcanoDrive',
};

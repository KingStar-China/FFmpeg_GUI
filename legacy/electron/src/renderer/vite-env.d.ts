import type { ElectronApi } from '../main/preload';

declare global {
  interface Window {
    electronAPI: ElectronApi;
  }
}

export {};

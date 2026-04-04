import { contextBridge, ipcRenderer } from 'electron';
import type {
  ExtractJob,
  ExtractTarget,
  MediaInfo,
  MuxJob,
  TaskHandle,
  TaskProgress,
  TrackInfo,
  ValidationIssue
} from '../shared/types';

export interface ElectronApi {
  pickInputFile: () => Promise<string | null>;
  pickExtraInputFiles: () => Promise<string[]>;
  inspectMedia: (inputPath: string, sourceIndex?: number) => Promise<MediaInfo>;
  validateMuxJob: (job: MuxJob, mediaList: MediaInfo[]) => Promise<ValidationIssue[]>;
  runMuxJob: (job: MuxJob, mediaList: MediaInfo[]) => Promise<TaskHandle>;
  suggestExtractTargets: (track: TrackInfo) => Promise<ExtractTarget[]>;
  validateExtractJob: (job: ExtractJob, media: MediaInfo) => Promise<ValidationIssue[]>;
  runExtractJob: (job: ExtractJob, media: MediaInfo) => Promise<TaskHandle>;
  pickOutputPath: (defaultPath: string, extensions: string[]) => Promise<string | null>;
  openOutputDirectory: (outputPath: string) => Promise<void>;
  onTaskProgress: (listener: (payload: TaskProgress) => void) => () => void;
}

const api: ElectronApi = {
  pickInputFile: () => ipcRenderer.invoke('pick-input-file'),
  pickExtraInputFiles: () => ipcRenderer.invoke('pick-extra-input-files'),
  inspectMedia: (inputPath, sourceIndex) => ipcRenderer.invoke('inspect-media', inputPath, sourceIndex),
  validateMuxJob: (job, mediaList) => ipcRenderer.invoke('validate-mux-job', job, mediaList),
  runMuxJob: (job, mediaList) => ipcRenderer.invoke('run-mux-job', job, mediaList),
  suggestExtractTargets: (track) => ipcRenderer.invoke('suggest-extract-targets', track),
  validateExtractJob: (job, media) => ipcRenderer.invoke('validate-extract-job', job, media),
  runExtractJob: (job, media) => ipcRenderer.invoke('run-extract-job', job, media),
  pickOutputPath: (defaultPath, extensions) => ipcRenderer.invoke('pick-output-path', defaultPath, extensions),
  openOutputDirectory: (outputPath) => ipcRenderer.invoke('open-output-directory', outputPath),
  onTaskProgress: (listener) => {
    const wrapped = (_event: Electron.IpcRendererEvent, payload: TaskProgress) => listener(payload);
    ipcRenderer.on('task-progress', wrapped);
    return () => {
      ipcRenderer.removeListener('task-progress', wrapped);
    };
  }
};

contextBridge.exposeInMainWorld('electronAPI', api);

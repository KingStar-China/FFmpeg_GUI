export type TrackKind = 'video' | 'audio' | 'subtitle' | 'data' | 'attachment' | 'chapter' | 'unknown';
export type ContainerKind = 'mp4' | 'mkv';
export type ValidationLevel = 'error' | 'warning';
export type TaskStatus = 'idle' | 'running' | 'completed' | 'failed';

export interface TrackDisposition {
  default: boolean;
  forced: boolean;
  hearingImpaired: boolean;
  visualImpaired: boolean;
}

export interface TrackInfo {
  streamIndex: number;
  kind: TrackKind;
  codec: string;
  codecLongName?: string;
  language?: string;
  title?: string;
  supported: boolean;
  supportNote?: string;
  disposition: TrackDisposition;
  synthetic?: boolean;
  sourceIndex: number;
  sourcePath: string;
  sourceFileName: string;
  trackKey: string;
}

export interface MediaInfo {
  inputPath: string;
  fileName: string;
  formatName: string;
  durationSeconds?: number;
  sizeBytes?: number;
  tracks: TrackInfo[];
}

export interface ValidationIssue {
  level: ValidationLevel;
  message: string;
  trackIndex?: number;
  trackKey?: string;
}

export interface MuxJob {
  inputs: string[];
  outputPath: string;
  container: ContainerKind;
  selectedTrackKeys: string[];
}

export interface ExtractTarget {
  id: string;
  extension: string;
  mode: 'raw' | 'container' | 'convert';
  label: string;
  note?: string;
  ffmpegArgs?: string[];
}

export interface ExtractJob {
  inputPath: string;
  outputPath: string;
  trackIndex: number;
  target: ExtractTarget;
}

export interface TaskHandle {
  taskId: string;
  status: TaskStatus;
}

export interface TaskProgress {
  taskId: string;
  status: TaskStatus;
  line?: string;
  progress?: number;
  outputPath?: string;
  error?: string;
}

import { app, BrowserWindow, Menu, dialog, ipcMain, shell } from 'electron';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { access } from 'node:fs/promises';
import { constants as fsConstants } from 'node:fs';
import {
  buildExtractArgs,
  buildTaskProgressFromLine,
  buildMuxArgs,
  listExtractTargets,
  validateExtractJob,
  validateMuxJob
} from '../shared/ffmpeg';
import type {
  ExtractJob,
  MediaInfo,
  MuxJob,
  TaskHandle,
  TaskProgress,
  TrackDisposition,
  TrackInfo
} from '../shared/types';

let mainWindow: BrowserWindow | null = null;

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1240,
    height: 860,
    autoHideMenuBar: true,
    minWidth: 920,
    minHeight: 700,
    backgroundColor: '#0d1017',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  if (process.env.VITE_DEV_SERVER_URL) {
    void mainWindow.loadURL(process.env.VITE_DEV_SERVER_URL);
  } else {
    void mainWindow.loadFile(path.join(__dirname, '../../dist/renderer/index.html'));
  }
}

function sendTaskProgress(payload: TaskProgress): void {
  mainWindow?.webContents.send('task-progress', payload);
}

async function ensureExecutable(command: string): Promise<void> {
  await new Promise<void>((resolve, reject) => {
    const child = spawn(command, ['-version'], { windowsHide: true, stdio: 'ignore' });
    child.on('error', () => reject(new Error(`系统 PATH 中未找到 ${command}。`)));
    child.on('exit', (code) => {
      if (code === 0) {
        resolve();
        return;
      }
      reject(new Error(`${command} 无法正常启动。`));
    });
  });
}

async function inspectMediaFile(inputPath: string, sourceIndex = 0): Promise<MediaInfo> {
  await ensureExecutable('ffprobe');
  const args = ['-v', 'error', '-print_format', 'json', '-show_streams', '-show_format', '-show_chapters', inputPath];

  const stdout = await new Promise<string>((resolve, reject) => {
    const child = spawn('ffprobe', args, { windowsHide: true });
    let output = '';
    let error = '';

    child.stdout.on('data', (chunk: Buffer) => {
      output += chunk.toString();
    });

    child.stderr.on('data', (chunk: Buffer) => {
      error += chunk.toString();
    });

    child.on('error', (spawnError) => reject(spawnError));
    child.on('exit', (code) => {
      if (code === 0) {
        resolve(output);
        return;
      }
      reject(new Error(error || 'ffprobe 解析失败。'));
    });
  });

  const parsed = JSON.parse(stdout) as {
    format?: { format_name?: string; duration?: string; size?: string };
    streams?: Array<Record<string, unknown>>;
    chapters?: Array<Record<string, unknown>>;
  };

  const tracks: TrackInfo[] = [
    ...((parsed.streams ?? []).map((stream) => mapStreamToTrack(stream, inputPath, sourceIndex))),
    ...((parsed.chapters ?? []).map((chapter, chapterIndex) => mapChapterToTrack(chapter, chapterIndex, inputPath, sourceIndex)))
  ];

  return {
    inputPath,
    fileName: path.basename(inputPath),
    formatName: parsed.format?.format_name ?? 'unknown',
    durationSeconds: parsed.format?.duration ? Number(parsed.format.duration) : undefined,
    sizeBytes: parsed.format?.size ? Number(parsed.format.size) : undefined,
    tracks
  };
}

function mapDisposition(input: Record<string, unknown> | undefined): TrackDisposition {
  return {
    default: input?.default === 1,
    forced: input?.forced === 1,
    hearingImpaired: input?.hearing_impaired === 1,
    visualImpaired: input?.visual_impaired === 1
  };
}

function mapStreamToTrack(stream: Record<string, unknown>, sourcePath: string, sourceIndex: number): TrackInfo {
  const codecType = String(stream.codec_type ?? 'unknown');
  const tags = (stream.tags as Record<string, unknown> | undefined) ?? {};
  const disposition = mapDisposition(stream.disposition as Record<string, unknown> | undefined);
  const streamIndex = Number(stream.index ?? -1);
  const kind = normalizeTrackKind(codecType);
  const supported = kind === 'video' || kind === 'audio' || kind === 'subtitle';
  const sourceFileName = path.basename(sourcePath);

  return {
    streamIndex,
    kind,
    codec: String(stream.codec_name ?? 'unknown'),
    codecLongName: stream.codec_long_name ? String(stream.codec_long_name) : undefined,
    language: tags.language ? String(tags.language) : undefined,
    title: tags.title ? String(tags.title) : undefined,
    supported,
    supportNote: supported ? undefined : 'v1 不支持此类轨道',
    disposition,
    sourceIndex,
    sourcePath,
    sourceFileName,
    trackKey: `${sourceIndex}:${streamIndex}`
  };
}

function mapChapterToTrack(chapter: Record<string, unknown>, chapterIndex: number, sourcePath: string, sourceIndex: number): TrackInfo {
  const tags = (chapter.tags as Record<string, unknown> | undefined) ?? {};
  const sourceFileName = path.basename(sourcePath);
  return {
    streamIndex: -1000 - chapterIndex,
    kind: 'chapter',
    codec: 'chapter',
    codecLongName: 'Chapter metadata',
    language: undefined,
    title: tags.title ? String(tags.title) : `Chapter ${chapterIndex + 1}`,
    supported: false,
    supportNote: 'v1 不支持章节导出或编辑',
    disposition: {
      default: false,
      forced: false,
      hearingImpaired: false,
      visualImpaired: false
    },
    synthetic: true,
    sourceIndex,
    sourcePath,
    sourceFileName,
    trackKey: `${sourceIndex}:chapter:${chapterIndex}`
  };
}

function normalizeTrackKind(codecType: string): TrackInfo['kind'] {
  switch (codecType) {
    case 'video':
    case 'audio':
    case 'subtitle':
    case 'data':
    case 'attachment':
      return codecType;
    default:
      return 'unknown';
  }
}

async function chooseOutputPath(defaultPath: string, filters: Array<{ name: string; extensions: string[] }>): Promise<string | null> {
  const result = await dialog.showSaveDialog({ defaultPath, filters });
  return result.canceled ? null : result.filePath;
}

async function runFfmpegTask(taskId: string, args: string[], outputPath: string, durationSeconds?: number): Promise<TaskHandle> {
  await ensureExecutable('ffmpeg');
  const child = spawn('ffmpeg', args, { windowsHide: true });
  let stderrBuffer = '';

  child.stderr.on('data', (chunk: Buffer) => {
    stderrBuffer += chunk.toString();
    const lines = stderrBuffer.split(/\r?\n/);
    stderrBuffer = lines.pop() ?? '';
    for (const rawLine of lines) {
      const line = rawLine.trim();
      if (!line) {
        continue;
      }
      sendTaskProgress(buildTaskProgressFromLine(taskId, line, durationSeconds));
    }
  });

  child.on('error', (error) => {
    sendTaskProgress({ taskId, status: 'failed', error: error.message });
  });

  child.on('exit', (code) => {
    if (stderrBuffer.trim()) {
      sendTaskProgress(buildTaskProgressFromLine(taskId, stderrBuffer.trim(), durationSeconds));
    }
    if (code === 0) {
      sendTaskProgress({ taskId, status: 'completed', progress: 1, outputPath });
      return;
    }
    sendTaskProgress({ taskId, status: 'failed', error: `FFmpeg 退出码 ${code ?? 'unknown'}` });
  });

  return { taskId, status: 'running' };
}

ipcMain.handle('pick-input-file', async () => {
  const result = await dialog.showOpenDialog({
    properties: ['openFile'],
    filters: [
      { name: 'Media Files', extensions: ['mkv', 'mp4', 'mov', 'avi', 'ts', 'm2ts', 'flv', 'mp3', 'aac', 'flac', 'wav'] },
      { name: 'All Files', extensions: ['*'] }
    ]
  });
  return result.canceled ? null : result.filePaths[0] ?? null;
});

ipcMain.handle('pick-extra-input-files', async () => {
  const result = await dialog.showOpenDialog({
    properties: ['openFile', 'multiSelections'],
    filters: [
      { name: 'Media Files', extensions: ['mkv', 'mp4', 'mov', 'avi', 'ts', 'm2ts', 'flv', 'mp3', 'aac', 'flac', 'wav'] },
      { name: 'All Files', extensions: ['*'] }
    ]
  });
  return result.canceled ? [] : result.filePaths;
});

ipcMain.handle('inspect-media', async (_event, inputPath: string, sourceIndex = 0) => {
  await access(inputPath, fsConstants.R_OK);
  return inspectMediaFile(inputPath, sourceIndex);
});

ipcMain.handle('validate-mux-job', async (_event, job: MuxJob, mediaList: MediaInfo[]) => validateMuxJob(job, mediaList));

ipcMain.handle('run-mux-job', async (_event, job: MuxJob, mediaList: MediaInfo[]) => {
  const args = buildMuxArgs(job, mediaList);
  const taskId = `mux-${Date.now()}`;
  const durationSeconds = mediaList.reduce((max, item) => Math.max(max, item.durationSeconds ?? 0), 0) || undefined;
  return runFfmpegTask(taskId, args, job.outputPath, durationSeconds);
});

ipcMain.handle('suggest-extract-targets', async (_event, track: TrackInfo) => listExtractTargets(track));
ipcMain.handle('validate-extract-job', async (_event, job: ExtractJob, media: MediaInfo) => validateExtractJob(job, media));
ipcMain.handle('run-extract-job', async (_event, job: ExtractJob, media: MediaInfo) => {
  const args = buildExtractArgs(job);
  const taskId = `extract-${Date.now()}`;
  return runFfmpegTask(taskId, args, job.outputPath, media.durationSeconds);
});

ipcMain.handle('pick-output-path', async (_event, defaultPath: string, extensions: string[]) => {
  return chooseOutputPath(defaultPath, [{ name: 'Output File', extensions }]);
});

ipcMain.handle('open-output-directory', async (_event, outputPath: string) => {
  shell.showItemInFolder(outputPath);
});

app.whenReady().then(() => {
  Menu.setApplicationMenu(null);
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

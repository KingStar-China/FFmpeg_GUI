import type {
  ContainerKind,
  ExtractJob,
  ExtractTarget,
  MediaInfo,
  MuxJob,
  TaskProgress,
  TrackInfo,
  ValidationIssue
} from './types';

const SUPPORTED_TEXT_SUBTITLE_CODECS = new Set([
  'subrip',
  'srt',
  'ass',
  'ssa',
  'webvtt',
  'text',
  'mov_text'
]);

const COMMON_VIDEO_CONVERT_TARGETS: ExtractTarget[] = [
  { id: 'video-mp4-h264', extension: 'mp4', mode: 'convert', label: '转换为 MP4 (H.264)', ffmpegArgs: ['-c:v', 'libx264', '-pix_fmt', 'yuv420p'] },
  { id: 'video-webm-vp9', extension: 'webm', mode: 'convert', label: '转换为 WebM (VP9)', ffmpegArgs: ['-c:v', 'libvpx-vp9'] },
  { id: 'video-avi-mpeg4', extension: 'avi', mode: 'convert', label: '转换为 AVI (MPEG-4)', ffmpegArgs: ['-c:v', 'mpeg4'] }
];

const COMMON_AUDIO_CONVERT_TARGETS: ExtractTarget[] = [
  { id: 'audio-mp3', extension: 'mp3', mode: 'convert', label: '转换为 MP3', ffmpegArgs: ['-c:a', 'libmp3lame'] },
  { id: 'audio-aac', extension: 'aac', mode: 'convert', label: '转换为 AAC', ffmpegArgs: ['-c:a', 'aac'] },
  { id: 'audio-flac', extension: 'flac', mode: 'convert', label: '转换为 FLAC', ffmpegArgs: ['-c:a', 'flac'] },
  { id: 'audio-wav', extension: 'wav', mode: 'convert', label: '转换为 WAV', ffmpegArgs: ['-c:a', 'pcm_s16le'] },
  { id: 'audio-opus', extension: 'opus', mode: 'convert', label: '转换为 Opus', ffmpegArgs: ['-c:a', 'libopus'] }
];

const COMMON_TEXT_SUBTITLE_CONVERT_TARGETS: ExtractTarget[] = [
  { id: 'subtitle-srt', extension: 'srt', mode: 'convert', label: '转换为 SRT', ffmpegArgs: ['-c:s', 'srt'] },
  { id: 'subtitle-ass', extension: 'ass', mode: 'convert', label: '转换为 ASS', ffmpegArgs: ['-c:s', 'ass'] },
  { id: 'subtitle-vtt', extension: 'vtt', mode: 'convert', label: '转换为 WebVTT', ffmpegArgs: ['-c:s', 'webvtt'] }
];

export function isSelectableTrack(track: TrackInfo): boolean {
  return track.supported && (track.kind === 'video' || track.kind === 'audio' || track.kind === 'subtitle');
}

export function isMp4TextSubtitle(track: TrackInfo): boolean {
  return track.kind === 'subtitle' && SUPPORTED_TEXT_SUBTITLE_CODECS.has(track.codec);
}

export function validateMuxJob(job: MuxJob, mediaList: MediaInfo[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const selected = getSelectedTracksForMux(mediaList, job.selectedTrackKeys);

  if (!job.outputPath.trim()) {
    issues.push({ level: 'error', message: '请先选择输出文件。' });
  }

  if (selected.length === 0) {
    issues.push({ level: 'error', message: '至少保留一条可支持轨道。' });
    return issues;
  }

  const selectedVideoTracks = selected.filter((track) => track.kind === 'video');
  if (selectedVideoTracks.length > 1) {
    issues.push({ level: 'error', message: '封装时最多只能选择 1 条视频轨。' });
  }

  for (const track of selected) {
    if (!isSelectableTrack(track)) {
      issues.push({
        level: 'error',
        message: `轨道 ${track.trackKey} 当前版本不支持封装。`,
        trackIndex: track.streamIndex,
        trackKey: track.trackKey
      });
    }

    if (job.container === 'mp4' && track.kind === 'subtitle' && !isMp4TextSubtitle(track)) {
      issues.push({
        level: 'error',
        message: `轨道 ${track.trackKey} 的字幕编码 ${track.codec} 不兼容 MP4 软字幕。`,
        trackIndex: track.streamIndex,
        trackKey: track.trackKey
      });
    }
  }

  const hasAssSubtitle = selected.some((track) => track.kind === 'subtitle' && (track.codec === 'ass' || track.codec === 'ssa'));
  if (job.container === 'mp4' && hasAssSubtitle) {
    issues.push({
      level: 'warning',
      message: 'ASS/SSA 封装到 MP4 时会转成 mov_text，样式可能丢失。'
    });
  }

  return issues;
}

export function validateExtractJob(job: ExtractJob, media: MediaInfo): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const track = media.tracks.find((item) => item.streamIndex === job.trackIndex);

  if (!job.outputPath.trim()) {
    issues.push({ level: 'error', message: '请先选择输出文件。' });
  }

  if (!track) {
    issues.push({ level: 'error', message: '未找到要提取的轨道。' });
    return issues;
  }

  if (!isSelectableTrack(track)) {
    issues.push({
      level: 'error',
      message: `轨道 ${track.streamIndex} 当前版本不支持提取。`,
      trackIndex: track.streamIndex,
      trackKey: track.trackKey
    });
  }

  return issues;
}

export function getSelectedTracks(media: MediaInfo, indexes: number[]): TrackInfo[] {
  const indexSet = new Set(indexes);
  return media.tracks.filter((track) => indexSet.has(track.streamIndex));
}

export function getSelectedTracksForMux(mediaList: MediaInfo[], trackKeys: string[]): TrackInfo[] {
  const trackMap = new Map<string, TrackInfo>();
  for (const media of mediaList) {
    for (const track of media.tracks) {
      trackMap.set(track.trackKey, track);
    }
  }

  return trackKeys.map((trackKey) => trackMap.get(trackKey)).filter((track): track is TrackInfo => Boolean(track));
}

export function getMuxTracks(mediaList: MediaInfo[]): TrackInfo[] {
  return mediaList.flatMap((media) => media.tracks);
}

export function listExtractTargets(track: TrackInfo): ExtractTarget[] {
  if (track.kind === 'video') {
    return dedupeTargets([
      { id: 'video-mkv-copy', extension: 'mkv', mode: 'container', label: '原轨道 (单轨 MKV)', note: '视频轨默认回退到单轨 MKV。' },
      ...COMMON_VIDEO_CONVERT_TARGETS
    ]);
  }

  if (track.kind === 'audio') {
    const defaults = buildAudioDefaultTargets(track);
    return dedupeTargets([...defaults, ...COMMON_AUDIO_CONVERT_TARGETS]);
  }

  if (track.kind === 'subtitle') {
    const defaults = buildSubtitleDefaultTargets(track);
    const convertTargets = isMp4TextSubtitle(track) ? COMMON_TEXT_SUBTITLE_CONVERT_TARGETS : [];
    return dedupeTargets([...defaults, ...convertTargets]);
  }

  return [{ id: 'raw-bin', extension: 'bin', mode: 'raw', label: '原始文件' }];
}

export function buildMuxArgs(job: MuxJob, mediaList: MediaInfo[]): string[] {
  const selectedTracks = getSelectedTracksForMux(mediaList, job.selectedTrackKeys);
  const args = ['-y'];

  for (const inputPath of job.inputs) {
    args.push('-i', inputPath);
  }

  for (const track of selectedTracks) {
    args.push('-map', `${track.sourceIndex}:${track.streamIndex}`);
  }

  args.push('-c', 'copy');

  if (job.container === 'mp4' && selectedTracks.some((track) => track.kind === 'subtitle')) {
    args.push('-c:s', 'mov_text');
  }

  if (job.container === 'mp4') {
    args.push('-movflags', '+faststart');
  }

  args.push(job.outputPath);
  return args;
}

export function buildExtractArgs(job: ExtractJob): string[] {
  const args = ['-y', '-i', job.inputPath, '-map', `0:${job.trackIndex}`];

  if (job.target.mode === 'convert' && job.target.ffmpegArgs?.length) {
    args.push(...job.target.ffmpegArgs);
  } else {
    args.push('-c', 'copy');
  }

  args.push(job.outputPath);
  return args;
}

export function formatCommandPreview(executable: string, args: string[]): string {
  return [executable, ...args.map(quoteCommandArg)].join(' ');
}

export function quoteCommandArg(value: string): string {
  if (/^[a-zA-Z0-9_./:+-]+$/.test(value)) {
    return value;
  }

  return `"${value.replace(/"/g, '\\"')}"`;
}

export function buildMuxOutputPath(inputPath: string, container: ContainerKind): string {
  const parsed = splitFilePath(inputPath);
  return joinPath(parsed.dir, `${parsed.name}.muxed.${container}`);
}

export function buildExtractOutputPath(inputPath: string, track: TrackInfo, target: ExtractTarget): string {
  const parsed = splitFilePath(inputPath);
  return joinPath(parsed.dir, `${parsed.name}.track${track.streamIndex}.${target.extension}`);
}

export function parseTimestampToSeconds(raw: string): number {
  const match = raw.match(/(?<hours>\d+):(?<minutes>\d+):(?<seconds>\d+(?:\.\d+)?)/);
  if (!match?.groups) {
    return 0;
  }

  return Number(match.groups.hours) * 3600 + Number(match.groups.minutes) * 60 + Number(match.groups.seconds);
}

export function buildTaskProgressFromLine(taskId: string, line: string, durationSeconds?: number): TaskProgress {
  const progressMatch = line.match(/time=(\d+:\d+:\d+(?:\.\d+)?)/);
  const seconds = progressMatch ? parseTimestampToSeconds(progressMatch[1]) : undefined;
  const progress = durationSeconds && seconds !== undefined ? Math.max(0, Math.min(1, seconds / durationSeconds)) : undefined;

  return {
    taskId,
    status: 'running',
    line,
    progress
  };
}

function buildAudioDefaultTargets(track: TrackInfo): ExtractTarget[] {
  if (track.codec.startsWith('pcm_')) {
    return [{ id: 'audio-default-wav', extension: 'wav', mode: 'raw', label: '原格式 (WAV/PCM)' }];
  }

  const mapped: Record<string, ExtractTarget> = {
    aac: { id: 'audio-default-aac', extension: 'aac', mode: 'raw', label: '原格式 (AAC)' },
    mp3: { id: 'audio-default-mp3', extension: 'mp3', mode: 'raw', label: '原格式 (MP3)' },
    flac: { id: 'audio-default-flac', extension: 'flac', mode: 'raw', label: '原格式 (FLAC)' },
    ac3: { id: 'audio-default-ac3', extension: 'ac3', mode: 'raw', label: '原格式 (AC-3)' },
    eac3: { id: 'audio-default-eac3', extension: 'eac3', mode: 'raw', label: '原格式 (E-AC-3)' },
    opus: { id: 'audio-default-opus', extension: 'opus', mode: 'raw', label: '原格式 (Opus)' },
    vorbis: { id: 'audio-default-ogg', extension: 'ogg', mode: 'raw', label: '原格式 (Ogg Vorbis)' }
  };

  return [mapped[track.codec] ?? { id: 'audio-default-mka', extension: 'mka', mode: 'container', label: '原轨道 (单轨 MKA)', note: '该音频编码没有安全的裸流扩展名，回退为 MKA。' }];
}

function buildSubtitleDefaultTargets(track: TrackInfo): ExtractTarget[] {
  const mapped: Record<string, ExtractTarget> = {
    subrip: { id: 'subtitle-default-srt', extension: 'srt', mode: 'raw', label: '原格式 (SRT)' },
    srt: { id: 'subtitle-default-srt-2', extension: 'srt', mode: 'raw', label: '原格式 (SRT)' },
    ass: { id: 'subtitle-default-ass', extension: 'ass', mode: 'raw', label: '原格式 (ASS)' },
    ssa: { id: 'subtitle-default-ssa', extension: 'ass', mode: 'raw', label: '原格式 (ASS)' },
    webvtt: { id: 'subtitle-default-vtt', extension: 'vtt', mode: 'raw', label: '原格式 (WebVTT)' },
    mov_text: { id: 'subtitle-default-mp4text', extension: 'mks', mode: 'container', label: '原轨道 (MKS)', note: 'mov_text 原样抽出时回退为 MKS。' }
  };

  return [mapped[track.codec] ?? { id: 'subtitle-default-mks', extension: 'mks', mode: 'container', label: '原轨道 (MKS)', note: '该字幕编码不能安全直接落地为文本文件，回退为 MKS。' }];
}

function dedupeTargets(targets: ExtractTarget[]): ExtractTarget[] {
  const seen = new Set<string>();
  const result: ExtractTarget[] = [];

  for (const target of targets) {
    const key = `${target.extension}:${target.mode}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    result.push(target);
  }

  return result;
}

function splitFilePath(inputPath: string): { dir: string; name: string; extension: string; separator: string } {
  const normalized = inputPath.replace(/\\/g, '/');
  const separator = inputPath.includes('\\') ? '\\' : '/';
  const lastSlash = normalized.lastIndexOf('/');
  const dir = lastSlash >= 0 ? normalized.slice(0, lastSlash).replace(/\//g, separator) : '';
  const base = lastSlash >= 0 ? normalized.slice(lastSlash + 1) : normalized;
  const lastDot = base.lastIndexOf('.');
  const name = lastDot > 0 ? base.slice(0, lastDot) : base;
  const extension = lastDot > 0 ? base.slice(lastDot + 1) : '';
  return { dir, name, extension, separator };
}

function joinPath(dir: string, fileName: string): string {
  if (!dir) {
    return fileName;
  }
  const separator = dir.includes('\\') ? '\\' : '/';
  return `${dir}${separator}${fileName}`;
}



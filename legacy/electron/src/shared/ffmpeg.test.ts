import assert from 'node:assert/strict';
import {
  buildExtractArgs,
  buildExtractOutputPath,
  buildMuxArgs,
  buildMuxOutputPath,
  listExtractTargets,
  parseTimestampToSeconds,
  validateMuxJob
} from './ffmpeg';
import type { ExtractJob, MediaInfo, MuxJob, TrackInfo } from './types';

const results: string[] = [];
function runCase(name: string, fn: () => void): void { fn(); results.push(`PASS ${name}`); }

const videoTrack: TrackInfo = {
  streamIndex: 0,
  kind: 'video',
  codec: 'h264',
  supported: true,
  disposition: { default: true, forced: false, hearingImpaired: false, visualImpaired: false },
  sourceIndex: 0,
  sourcePath: 'D:/media/video1.mkv',
  sourceFileName: 'video1.mkv',
  trackKey: '0:0'
};
const audioTrack: TrackInfo = {
  streamIndex: 0,
  kind: 'audio',
  codec: 'aac',
  supported: true,
  disposition: { default: true, forced: false, hearingImpaired: false, visualImpaired: false },
  sourceIndex: 1,
  sourcePath: 'D:/media/audio2.mka',
  sourceFileName: 'audio2.mka',
  trackKey: '1:0'
};
const subtitleTrack: TrackInfo = {
  streamIndex: 2,
  kind: 'subtitle',
  codec: 'mov_text',
  supported: true,
  disposition: { default: false, forced: false, hearingImpaired: false, visualImpaired: false },
  sourceIndex: 0,
  sourcePath: 'D:/media/video1.mkv',
  sourceFileName: 'video1.mkv',
  trackKey: '0:2'
};
const imageSubtitle: TrackInfo = {
  streamIndex: 3,
  kind: 'subtitle',
  codec: 'hdmv_pgs_subtitle',
  supported: true,
  disposition: { default: false, forced: false, hearingImpaired: false, visualImpaired: false },
  sourceIndex: 0,
  sourcePath: 'D:/media/video1.mkv',
  sourceFileName: 'video1.mkv',
  trackKey: '0:3'
};
const secondVideoTrack: TrackInfo = {
  streamIndex: 1,
  kind: 'video',
  codec: 'hevc',
  supported: true,
  disposition: { default: false, forced: false, hearingImpaired: false, visualImpaired: false },
  sourceIndex: 1,
  sourcePath: 'D:/media/audio2.mka',
  sourceFileName: 'audio2.mka',
  trackKey: '1:1'
};

const media1: MediaInfo = {
  inputPath: 'D:/media/video1.mkv',
  fileName: 'video1.mkv',
  formatName: 'matroska',
  durationSeconds: 120,
  tracks: [videoTrack, subtitleTrack, imageSubtitle]
};
const media2: MediaInfo = {
  inputPath: 'D:/media/audio2.mka',
  fileName: 'audio2.mka',
  formatName: 'matroska',
  durationSeconds: 118,
  tracks: [audioTrack, secondVideoTrack]
};

runCase('封装支持跨文件选轨', () => {
  const job: MuxJob = { inputs: [media1.inputPath, media2.inputPath], outputPath: 'D:/media/out.mkv', container: 'mkv', selectedTrackKeys: ['0:0', '1:0'] };
  assert.deepEqual(buildMuxArgs(job, [media1, media2]), ['-y', '-i', 'D:/media/video1.mkv', '-i', 'D:/media/audio2.mka', '-map', '0:0', '-map', '1:0', '-c', 'copy', 'D:/media/out.mkv']);
});

runCase('封装顺序按已选轨道顺序输出', () => {
  const job: MuxJob = { inputs: [media1.inputPath, media2.inputPath], outputPath: 'D:/media/out-ordered.mkv', container: 'mkv', selectedTrackKeys: ['1:0', '0:0', '0:2'] };
  assert.deepEqual(buildMuxArgs(job, [media1, media2]), ['-y', '-i', 'D:/media/video1.mkv', '-i', 'D:/media/audio2.mka', '-map', '1:0', '-map', '0:0', '-map', '0:2', '-c', 'copy', 'D:/media/out-ordered.mkv']);
});

runCase('mp4 文本字幕允许封装', () => {
  const job: MuxJob = { inputs: [media1.inputPath, media2.inputPath], outputPath: 'D:/media/sample.muxed.mp4', container: 'mp4', selectedTrackKeys: ['0:0', '1:0', '0:2'] };
  const issues = validateMuxJob(job, [media1, media2]);
  assert.equal(issues.filter((issue) => issue.level === 'error').length, 0);
});

runCase('mp4 不允许图片字幕', () => {
  const job: MuxJob = { inputs: [media1.inputPath], outputPath: 'D:/media/sample.muxed.mp4', container: 'mp4', selectedTrackKeys: ['0:0', '0:3'] };
  const issues = validateMuxJob(job, [media1]);
  assert.equal(issues.some((issue) => issue.level === 'error' && issue.trackKey === '0:3'), true);
});

runCase('封装不允许同时选择两条视频轨', () => {
  const job: MuxJob = { inputs: [media1.inputPath, media2.inputPath], outputPath: 'D:/media/out.mkv', container: 'mkv', selectedTrackKeys: ['0:0', '1:1'] };
  const issues = validateMuxJob(job, [media1, media2]);
  assert.equal(issues.some((issue) => issue.level === 'error' && issue.message.includes('最多只能选择 1 条视频轨')), true);
});

runCase('视频提取包含默认格式和可转换格式', () => {
  const targets = listExtractTargets(videoTrack);
  assert.equal(targets[0].extension, 'mkv');
  assert.equal(targets.some((target) => target.extension === 'mp4' && target.mode === 'convert'), true);
});

runCase('视频转换命令会使用目标视频编码', () => {
  const target = listExtractTargets(videoTrack).find((item) => item.extension === 'mp4' && item.mode === 'convert');
  assert.ok(target);
  const job: ExtractJob = { inputPath: media1.inputPath, outputPath: buildExtractOutputPath(media1.inputPath, videoTrack, target), trackIndex: 0, target };
  assert.deepEqual(buildExtractArgs(job), ['-y', '-i', 'D:/media/video1.mkv', '-map', '0:0', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', 'D:/media/video1.track0.mp4']);
});

runCase('AAC 提取包含默认格式和可转换格式', () => {
  const targets = listExtractTargets(audioTrack);
  assert.equal(targets[0].extension, 'aac');
  assert.equal(targets.some((target) => target.extension === 'mp3' && target.mode === 'convert'), true);
});

runCase('mov_text 字幕既保留默认回退也支持转成文本字幕', () => {
  const targets = listExtractTargets(subtitleTrack);
  assert.equal(targets[0].extension, 'mks');
  assert.equal(targets.some((target) => target.extension === 'srt' && target.mode === 'convert'), true);
});

runCase('字幕转换命令会使用目标编码', () => {
  const target = listExtractTargets(subtitleTrack).find((item) => item.extension === 'srt' && item.mode === 'convert');
  assert.ok(target);
  const job: ExtractJob = { inputPath: media1.inputPath, outputPath: buildExtractOutputPath(media1.inputPath, subtitleTrack, target), trackIndex: subtitleTrack.streamIndex, target };
  assert.deepEqual(buildExtractArgs(job), ['-y', '-i', 'D:/media/video1.mkv', '-map', '0:2', '-c:s', 'srt', 'D:/media/video1.track2.srt']);
});

runCase('默认输出文件名和时间解析正确', () => {
  assert.equal(buildMuxOutputPath(media1.inputPath, 'mkv'), 'D:/media/video1.muxed.mkv');
  assert.equal(parseTimestampToSeconds('00:01:02.50'), 62.5);
});

for (const line of results) console.log(line);
console.log(`TOTAL ${results.length}`);

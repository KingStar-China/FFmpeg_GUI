import { startTransition, useEffect, useEffectEvent, useMemo, useState } from 'react';
import {
  buildExtractArgs,
  buildExtractOutputPath,
  buildMuxArgs,
  buildMuxOutputPath,
  formatCommandPreview,
  getMuxTracks,
  getSelectedTracks,
  getSelectedTracksForMux,
  isSelectableTrack,
  listExtractTargets,
  validateExtractJob,
  validateMuxJob
} from '../shared/ffmpeg';
import type {
  ContainerKind,
  ExtractJob,
  MediaInfo,
  MuxJob,
  TaskProgress,
  TaskStatus,
  TrackInfo,
  ValidationIssue
} from '../shared/types';

type WorkMode = 'mux' | 'extract';

function getTrackKindLabel(kind: TrackInfo['kind']): string {
  switch (kind) {
    case 'video': return '视频';
    case 'audio': return '音频';
    case 'subtitle': return '字幕';
    case 'data': return '数据';
    case 'attachment': return '附件';
    case 'chapter': return '章节';
    default: return '未知';
  }
}

const EMPTY_LOGS: string[] = [];

function buildDefaultMuxTrackKeys(mediaList: MediaInfo[]): string[] {
  let hasVideo = false;
  const selected: string[] = [];
  for (const media of mediaList) {
    for (const track of media.tracks) {
      if (!isSelectableTrack(track)) continue;
      if (track.kind === 'video') {
        if (hasVideo) continue;
        hasVideo = true;
      }
      selected.push(track.trackKey);
    }
  }
  return selected;
}

function moveItem<T>(items: T[], index: number, direction: -1 | 1): T[] {
  const nextIndex = index + direction;
  if (nextIndex < 0 || nextIndex >= items.length) return items;
  const clone = [...items];
  const [item] = clone.splice(index, 1);
  clone.splice(nextIndex, 0, item);
  return clone;
}

function App() {
  const [media, setMedia] = useState<MediaInfo | null>(null);
  const [muxMediaList, setMuxMediaList] = useState<MediaInfo[]>([]);
  const [mode, setMode] = useState<WorkMode>('mux');
  const [container, setContainer] = useState<ContainerKind>('mkv');
  const [selectedTrackKeys, setSelectedTrackKeys] = useState<string[]>([]);
  const [selectedTracks, setSelectedTracks] = useState<number[]>([]);
  const [extractTargetId, setExtractTargetId] = useState<string>('');
  const [outputPath, setOutputPath] = useState('');
  const [outputDirty, setOutputDirty] = useState(false);
  const [activeTaskId, setActiveTaskId] = useState<string | null>(null);
  const [taskStatus, setTaskStatus] = useState<TaskStatus>('idle');
  const [taskLogs, setTaskLogs] = useState<string[]>(EMPTY_LOGS);
  const [taskProgress, setTaskProgress] = useState<number | undefined>();
  const [taskError, setTaskError] = useState<string | null>(null);
  const [lastOutputPath, setLastOutputPath] = useState<string | null>(null);
  const [globalError, setGlobalError] = useState<string | null>(null);

  const muxTracks = useMemo(() => getMuxTracks(muxMediaList), [muxMediaList]);
  const selectedMuxTracks = useMemo(() => getSelectedTracksForMux(muxMediaList, selectedTrackKeys), [muxMediaList, selectedTrackKeys]);
  const selectableTracks = useMemo(() => media?.tracks.filter((track) => isSelectableTrack(track)) ?? [], [media]);
  const selectedSelectableTracks = useMemo(() => (media ? getSelectedTracks(media, selectedTracks).filter((track) => isSelectableTrack(track)) : []), [media, selectedTracks]);
  const extractTrack = mode === 'extract' && selectedSelectableTracks.length === 1 ? selectedSelectableTracks[0] : null;
  const extractTargets = useMemo(() => (extractTrack ? listExtractTargets(extractTrack) : []), [extractTrack]);
  const extractTarget = extractTargets.find((target) => target.id === extractTargetId) ?? extractTargets[0] ?? null;

  const muxJob: MuxJob | null = muxMediaList.length > 0 && mode === 'mux' ? { inputs: muxMediaList.map((item) => item.inputPath), outputPath, container, selectedTrackKeys } : null;
  const extractJob: ExtractJob | null = media && mode === 'extract' && extractTrack && extractTarget ? { inputPath: media.inputPath, outputPath, trackIndex: extractTrack.streamIndex, target: extractTarget } : null;

  const validationIssues = useMemo(() => {
    if (mode === 'mux') return muxJob ? validateMuxJob(muxJob, muxMediaList) : [];
    if (!media) return [] as ValidationIssue[];
    const issues: ValidationIssue[] = [];
    if (selectedSelectableTracks.length !== 1) issues.push({ level: 'error', message: '提取模式必须只保留 1 条可支持轨道。' });
    if (extractJob) issues.push(...validateExtractJob(extractJob, media));
    return issues;
  }, [extractJob, media, mode, muxJob, muxMediaList, selectedSelectableTracks.length]);

  const commandPreview = useMemo(() => {
    if (mode === 'mux') return muxJob ? formatCommandPreview('ffmpeg', buildMuxArgs(muxJob, muxMediaList)) : '导入媒体文件后显示 ffmpeg 命令预览';
    if (mode === 'extract' && extractJob) return formatCommandPreview('ffmpeg', buildExtractArgs(extractJob));
    return '当前选择不足以生成命令';
  }, [extractJob, mode, muxJob, muxMediaList]);

  const applySuggestedOutput = useEffectEvent((nextMode: WorkMode, nextMedia: MediaInfo | null, nextMuxMediaList: MediaInfo[], nextContainer: ContainerKind, nextSelected: number[], nextTargetId: string) => {
    if (outputDirty) return;
    if (nextMode === 'mux') {
      const primary = nextMuxMediaList[0];
      setOutputPath(primary ? buildMuxOutputPath(primary.inputPath, nextContainer) : '');
      return;
    }
    if (!nextMedia) {
      setOutputPath('');
      return;
    }
    const track = getSelectedTracks(nextMedia, nextSelected).find((item) => isSelectableTrack(item));
    if (!track) {
      setOutputPath('');
      return;
    }
    const targets = listExtractTargets(track);
    const target = targets.find((item) => item.id === nextTargetId) ?? targets[0];
    setOutputPath(target ? buildExtractOutputPath(nextMedia.inputPath, track, target) : '');
  });

  useEffect(() => window.electronAPI.onTaskProgress((payload: TaskProgress) => {
    if (payload.taskId !== activeTaskId) return;
    if (payload.line) setTaskLogs((current) => [...current.slice(-299), payload.line]);
    if (payload.progress !== undefined) setTaskProgress(payload.progress);
    if (payload.status === 'completed') {
      setTaskStatus('completed');
      setTaskError(null);
      setTaskProgress(1);
      setLastOutputPath(payload.outputPath ?? null);
    }
    if (payload.status === 'failed') {
      setTaskStatus('failed');
      setTaskError(payload.error ?? '任务失败');
    }
  }), [activeTaskId]);

  useEffect(() => {
    if (!extractTargets.length) {
      if (extractTargetId) setExtractTargetId('');
      return;
    }
    if (!extractTargets.some((target) => target.id === extractTargetId)) setExtractTargetId(extractTargets[0].id);
  }, [extractTargetId, extractTargets]);

  useEffect(() => {
    applySuggestedOutput(mode, media, muxMediaList, container, selectedTracks, extractTargetId);
  }, [applySuggestedOutput, container, extractTargetId, media, mode, muxMediaList, selectedTracks]);

  const resetTaskState = () => {
    setTaskLogs(EMPTY_LOGS);
    setTaskStatus('idle');
    setTaskError(null);
    setTaskProgress(undefined);
    setLastOutputPath(null);
  };

  const handlePickInput = async () => {
    try {
      setGlobalError(null);
      const inputPath = await window.electronAPI.pickInputFile();
      if (!inputPath) return;
      const nextMedia = await window.electronAPI.inspectMedia(inputPath, 0);
      const defaultSelectedTracks = nextMedia.tracks.filter((track) => isSelectableTrack(track)).map((track) => track.streamIndex);
      const defaultSelectedKeys = buildDefaultMuxTrackKeys([nextMedia]);
      startTransition(() => {
        setMedia(nextMedia);
        setMuxMediaList([nextMedia]);
        setMode('mux');
        setContainer('mkv');
        setSelectedTracks(defaultSelectedTracks);
        setSelectedTrackKeys(defaultSelectedKeys);
        setExtractTargetId('');
        setOutputDirty(false);
        resetTaskState();
      });
    } catch (error) {
      setGlobalError(error instanceof Error ? error.message : '导入媒体文件失败。');
    }
  };

  const handleAddMuxInputs = async () => {
    try {
      const paths = await window.electronAPI.pickExtraInputFiles();
      const existing = new Set(muxMediaList.map((item) => item.inputPath));
      const newPaths = paths.filter((item) => !existing.has(item));
      if (!newPaths.length) return;
      const loaded = await Promise.all(newPaths.map((inputPath, index) => window.electronAPI.inspectMedia(inputPath, muxMediaList.length + index)));
      const nextMuxMediaList = [...muxMediaList, ...loaded];
      setMuxMediaList(nextMuxMediaList);
      setSelectedTrackKeys(buildDefaultMuxTrackKeys(nextMuxMediaList));
      setOutputDirty(false);
    } catch (error) {
      setGlobalError(error instanceof Error ? error.message : '添加媒体文件失败。');
    }
  };

  const handleModeChange = (nextMode: WorkMode) => {
    setMode(nextMode);
    setOutputDirty(false);
    if (!media) return;
    if (nextMode === 'extract') {
      const preferred = selectedSelectableTracks[0] ?? selectableTracks[0] ?? null;
      setSelectedTracks(preferred ? [preferred.streamIndex] : []);
      setExtractTargetId('');
    } else {
      setSelectedTrackKeys(buildDefaultMuxTrackKeys(muxMediaList));
    }
  };

  const handleToggleMuxTrack = (track: TrackInfo) => {
    if (!isSelectableTrack(track)) return;
    setSelectedTrackKeys((current) => {
      const exists = current.includes(track.trackKey);
      if (exists) return current.filter((value) => value !== track.trackKey);
      if (track.kind === 'video') {
        const next = current.filter((value) => {
          const currentTrack = muxTracks.find((item) => item.trackKey === value);
          return currentTrack?.kind !== 'video';
        });
        return [...next, track.trackKey];
      }
      return [...current, track.trackKey];
    });
  };

  const handleMoveMuxTrack = (trackKey: string, direction: -1 | 1) => {
    setSelectedTrackKeys((current) => moveItem(current, current.indexOf(trackKey), direction));
  };

  const handleToggleExtractTrack = (track: TrackInfo) => {
    if (!isSelectableTrack(track)) return;
    setSelectedTracks((current) => {
      const exists = current.includes(track.streamIndex);
      setExtractTargetId('');
      return exists ? [] : [track.streamIndex];
    });
  };

  const handlePickOutput = async () => {
    const extensions = mode === 'mux' ? [container] : extractTarget ? [extractTarget.extension] : ['mkv'];
    const picked = await window.electronAPI.pickOutputPath(outputPath || 'output', extensions);
    if (!picked) return;
    setOutputPath(picked);
    setOutputDirty(true);
  };

  const handleRun = async () => {
    if (validationIssues.some((issue) => issue.level === 'error')) {
      setTaskError('请先解决校验错误后再执行。');
      setTaskStatus('failed');
      return;
    }
    try {
      setTaskLogs(EMPTY_LOGS);
      setTaskError(null);
      setTaskStatus('running');
      setTaskProgress(0);
      setLastOutputPath(null);
      if (mode === 'mux' && muxJob) {
        const handle = await window.electronAPI.runMuxJob(muxJob, muxMediaList);
        setActiveTaskId(handle.taskId);
        return;
      }
      if (mode === 'extract' && extractJob && media) {
        const handle = await window.electronAPI.runExtractJob(extractJob, media);
        setActiveTaskId(handle.taskId);
      }
    } catch (error) {
      setTaskStatus('failed');
      setTaskError(error instanceof Error ? error.message : '执行任务失败。');
    }
  };

  const displayTracks = mode === 'mux' ? muxTracks : (media?.tracks ?? []);

  return (
    <div className="app-shell">
      <section className="hero-card panel">
        <div>
          <p className="eyebrow">FFmpeg GUI v1</p>
          <h1>轨道封装 / 提取工作台</h1>
          <p className="hero-copy">封装支持主文件加额外媒体文件混流，提取仍按主文件单轨处理。</p>
        </div>
        <div className="hero-actions">
          <div className="hero-button-row">
            <button className="primary-button" onClick={handlePickInput}>导入主文件</button>
            <button className="secondary-button" onClick={handleAddMuxInputs} disabled={!media}>添加媒体文件</button>
          </div>
          <div className="hero-meta">
            <span>容器：MP4 / MKV</span>
            <span>封装：可跨文件选轨</span>
          </div>
        </div>
      </section>

      {globalError ? <section className="panel error-banner">{globalError}</section> : null}

      <div className="main-grid">
        <section className="panel stack-gap">
          <div className="section-head">
            <div>
              <p className="section-kicker">Input</p>
              <h2>媒体概览</h2>
            </div>
            <div className="mode-switch">
              <button className={mode === 'mux' ? 'mode-button active' : 'mode-button'} onClick={() => handleModeChange('mux')}>封装</button>
              <button className={mode === 'extract' ? 'mode-button active' : 'mode-button'} onClick={() => handleModeChange('extract')} disabled={!media}>提取</button>
            </div>
          </div>

          {mode === 'mux' ? (
            <div className="source-list">
              {muxMediaList.length > 0 ? muxMediaList.map((item) => (
                <div key={item.inputPath} className="source-chip">
                  <span className="summary-label">输入 {item.tracks[0]?.sourceIndex ?? 0}</span>
                  <strong>{item.fileName}</strong>
                </div>
              )) : <div className="empty-state">还没有导入文件。</div>}
            </div>
          ) : media ? (
            <div className="media-summary">
              <div><span className="summary-label">文件</span><strong>{media.fileName}</strong></div>
              <div><span className="summary-label">格式</span><strong>{media.formatName}</strong></div>
              <div><span className="summary-label">时长</span><strong>{media.durationSeconds ? `${media.durationSeconds.toFixed(2)}s` : '未知'}</strong></div>
              <div><span className="summary-label">轨道数</span><strong>{media.tracks.length}</strong></div>
            </div>
          ) : <div className="empty-state">还没有导入文件。</div>}

          <div className="track-table-wrap">
            <table className="track-table">
              <thead>
                <tr>
                  <th>保留</th>
                  {mode === 'mux' ? <th>来源</th> : null}
                  <th>轨道</th>
                  <th>类型</th>
                  <th>Codec</th>
                  <th>语言</th>
                  <th>标题</th>
                  <th>标记</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                {displayTracks.map((track) => {
                  const selected = mode === 'mux' ? selectedTrackKeys.includes(track.trackKey) : selectedTracks.includes(track.streamIndex);
                  const flags = [track.disposition.default ? 'default' : null, track.disposition.forced ? 'forced' : null, track.disposition.hearingImpaired ? 'HI' : null, track.disposition.visualImpaired ? 'VI' : null].filter(Boolean).join(', ');
                  return (
                    <tr key={track.trackKey} className={!track.supported ? 'disabled-row' : ''}>
                      <td><input type="checkbox" checked={selected} disabled={!isSelectableTrack(track)} onChange={() => mode === 'mux' ? handleToggleMuxTrack(track) : handleToggleExtractTrack(track)} /></td>
                      {mode === 'mux' ? <td title={track.sourcePath}>{track.sourceIndex}. {track.sourceFileName}</td> : null}
                      <td>{track.synthetic ? `C${Math.abs(track.streamIndex + 999)}` : track.streamIndex}</td>
                      <td><span className={`kind-tag ${track.kind}`}>{getTrackKindLabel(track.kind)}</span></td>
                      <td title={track.codecLongName}>{track.codec}</td>
                      <td>{track.language ?? '-'}</td>
                      <td>{track.title ?? '-'}</td>
                      <td>{flags || '-'}</td>
                      <td>{track.supported ? '可用' : track.supportNote ?? 'v1 不支持'}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel stack-gap">
          <div className="section-head compact"><div><p className="section-kicker">Output</p><h2>{mode === 'mux' ? '封装设置' : '提取设置'}</h2></div></div>

          {mode === 'mux' ? (
            <>
              <label className="field"><span>输出容器</span><select value={container} onChange={(event) => setContainer(event.target.value as ContainerKind)}><option value="mkv">MKV</option><option value="mp4">MP4</option></select></label>
              <div className="issues-panel">
                <div className="issues-head"><h3>已选轨道顺序</h3><span>{selectedMuxTracks.length} 条</span></div>
                {selectedMuxTracks.length > 0 ? (
                  <div className="mux-order-list">
                    {selectedMuxTracks.map((track, index) => (
                      <div key={track.trackKey} className="mux-order-item">
                        <div className="mux-order-main">
                          <span className="mux-order-index">{index + 1}</span>
                          <span className={`kind-tag ${track.kind}`}>{getTrackKindLabel(track.kind)}</span>
                          <span className="mux-order-text" title={`${track.sourceFileName} / ${track.codec}`}>{track.sourceFileName} / {track.codec}</span>
                        </div>
                        <div className="mux-order-actions">
                          <button className="secondary-button order-button" onClick={() => handleMoveMuxTrack(track.trackKey, -1)} disabled={index === 0}>上移</button>
                          <button className="secondary-button order-button" onClick={() => handleMoveMuxTrack(track.trackKey, 1)} disabled={index === selectedMuxTracks.length - 1}>下移</button>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : <div className="empty-mini">还没有选中要封装的轨道。</div>}
              </div>
            </>
          ) : (
            <>
              <div className="extract-card">
                <span>当前提取轨道</span>
                <strong>{extractTrack ? `${getTrackKindLabel(extractTrack.kind)} #${extractTrack.streamIndex} (${extractTrack.codec})` : '请只保留 1 条轨道'}</strong>
                <span>{extractTarget ? `${extractTarget.label} -> .${extractTarget.extension}` : '尚未生成输出建议'}</span>
                {extractTarget?.note ? <small>{extractTarget.note}</small> : null}
              </div>
              <label className="field"><span>输出格式</span><select value={extractTarget?.id ?? ''} onChange={(event) => { setExtractTargetId(event.target.value); setOutputDirty(false); }} disabled={!extractTrack || extractTargets.length === 0}>{extractTargets.map((target) => <option key={target.id} value={target.id}>{target.label} (*.{target.extension})</option>)}</select></label>
            </>
          )}

          <label className="field">
            <span>输出文件</span>
            <div className="path-row">
              <input value={outputPath} onChange={(event) => { setOutputPath(event.target.value); setOutputDirty(true); }} placeholder="选择输出路径" />
              <button className="secondary-button" onClick={handlePickOutput} disabled={mode === 'mux' ? muxMediaList.length === 0 : !media}>浏览</button>
            </div>
          </label>

          <div className="issues-panel">
            <div className="issues-head"><h3>规则校验</h3><span>{validationIssues.length} 项</span></div>
            {validationIssues.length > 0 ? <ul className="issues-list">{validationIssues.map((issue, index) => <li key={`${issue.message}-${index}`} className={issue.level === 'error' ? 'issue error' : 'issue warning'}><strong>{issue.level === 'error' ? '错误' : '提示'}</strong><span>{issue.message}</span></li>)}</ul> : <div className="empty-mini">当前没有阻断错误。</div>}
          </div>

          <div className="command-preview"><div className="issues-head"><h3>命令预览</h3></div><pre>{commandPreview}</pre></div>
          <button className="primary-button wide" onClick={handleRun} disabled={(mode === 'mux' ? muxMediaList.length === 0 : !media) || taskStatus === 'running'}>{taskStatus === 'running' ? '执行中...' : mode === 'mux' ? '开始封装' : '开始提取'}</button>
        </section>
      </div>

      <section className="panel stack-gap">
        <div className="section-head compact"><div><p className="section-kicker">Task</p><h2>执行面板</h2></div><div className="task-badges"><span className={`status-pill ${taskStatus}`}>{taskStatus}</span><span>{taskProgress !== undefined ? `${Math.round(taskProgress * 100)}%` : '--'}</span></div></div>
        <div className="progress-rail"><div className="progress-fill" style={{ width: `${Math.max(0, Math.min(100, Math.round((taskProgress ?? 0) * 100)))}%` }} /></div>
        {taskError ? <div className="error-banner">{taskError}</div> : null}
        {lastOutputPath ? <div className="result-row"><span>输出完成：{lastOutputPath}</span><button className="secondary-button" onClick={() => void window.electronAPI.openOutputDirectory(lastOutputPath)}>打开输出目录</button></div> : null}
        <div className="log-box">{taskLogs.length > 0 ? taskLogs.map((line, index) => <div key={`${line}-${index}`}>{line}</div>) : '执行日志会显示在这里'}</div>
      </section>
    </div>
  );
}

export default App;

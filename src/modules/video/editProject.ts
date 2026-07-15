export type VideoEditMode = 'lossless' | 'precise';
export type VideoEditOutputMode = 'merge';

export interface VideoEditSegment {
  id: string;
  startSecs: number;
  endSecs: number;
  included: boolean;
}

export interface VideoEditProject {
  inputPath: string;
  durationSecs: number;
  fps: number;
  segments: VideoEditSegment[];
  mode: VideoEditMode;
  outputMode: VideoEditOutputMode;
  outputPath: string;
}

export interface CreateVideoEditProjectOptions {
  inputPath: string;
  durationSecs: number;
  fps: number;
  outputPath: string;
  mode?: VideoEditMode;
}

export interface VideoEditRange {
  startSecs: number;
  endSecs: number;
}

export interface VideoEditHistory {
  past: VideoEditProject[];
  present: VideoEditProject;
  future: VideoEditProject[];
}

const TIME_EPSILON = 1e-6;

const finiteNonNegative = (value: number): number =>
  Number.isFinite(value) ? Math.max(0, value) : 0;

const clamp = (value: number, minimum: number, maximum: number): number =>
  Math.min(maximum, Math.max(minimum, value));

export const cloneVideoEditProject = (project: VideoEditProject): VideoEditProject => ({
  ...project,
  segments: project.segments.map((segment) => ({ ...segment })),
});

export const createVideoEditProject = ({
  inputPath,
  durationSecs,
  fps,
  outputPath,
  mode = 'lossless',
}: CreateVideoEditProjectOptions): VideoEditProject => {
  const safeDuration = finiteNonNegative(durationSecs);

  return {
    inputPath,
    durationSecs: safeDuration,
    fps: finiteNonNegative(fps),
    segments: [
      {
        id: 'segment-1',
        startSecs: 0,
        endSecs: safeDuration,
        included: true,
      },
    ],
    mode,
    outputMode: 'merge',
    outputPath,
  };
};

const nextSegmentId = (segments: VideoEditSegment[]): string => {
  const highestId = segments.reduce((highest, segment) => {
    const match = /^segment-(\d+)$/.exec(segment.id);
    return match ? Math.max(highest, Number(match[1])) : highest;
  }, 0);

  return `segment-${highestId + 1}`;
};

export const splitAtPlayhead = (
  project: VideoEditProject,
  playheadSecs: number,
): VideoEditProject => {
  const time = clamp(finiteNonNegative(playheadSecs), 0, project.durationSecs);
  const segmentIndex = project.segments.findIndex(
    (segment) =>
      time > segment.startSecs + TIME_EPSILON && time < segment.endSecs - TIME_EPSILON,
  );

  if (segmentIndex < 0) return project;

  const segment = project.segments[segmentIndex];
  const left: VideoEditSegment = { ...segment, endSecs: time };
  const right: VideoEditSegment = {
    ...segment,
    id: nextSegmentId(project.segments),
    startSecs: time,
  };

  return {
    ...project,
    segments: [
      ...project.segments.slice(0, segmentIndex),
      left,
      right,
      ...project.segments.slice(segmentIndex + 1),
    ],
  };
};

export const setSegmentIncluded = (
  project: VideoEditProject,
  segmentId: string,
  included: boolean,
): VideoEditProject => {
  const segmentIndex = project.segments.findIndex((segment) => segment.id === segmentId);
  if (segmentIndex < 0 || project.segments[segmentIndex].included === included) return project;

  return {
    ...project,
    segments: project.segments.map((segment, index) =>
      index === segmentIndex ? { ...segment, included } : { ...segment },
    ),
  };
};

export const deleteSegment = (project: VideoEditProject, segmentId: string): VideoEditProject =>
  setSegmentIncluded(project, segmentId, false);

export const restoreSegment = (project: VideoEditProject, segmentId: string): VideoEditProject =>
  setSegmentIncluded(project, segmentId, true);

export const trimToInPoint = (
  project: VideoEditProject,
  inPointSecs: number,
): VideoEditProject => {
  const time = clamp(finiteNonNegative(inPointSecs), 0, project.durationSecs);
  const split = splitAtPlayhead(project, time);
  let changed = false;
  const segments = split.segments.map((segment) => {
    if (segment.endSecs <= time + TIME_EPSILON && segment.included) {
      changed = true;
      return { ...segment, included: false };
    }
    return { ...segment };
  });

  return changed || split !== project ? { ...split, segments } : project;
};

export const trimToOutPoint = (
  project: VideoEditProject,
  outPointSecs: number,
): VideoEditProject => {
  const time = clamp(finiteNonNegative(outPointSecs), 0, project.durationSecs);
  const split = splitAtPlayhead(project, time);
  let changed = false;
  const segments = split.segments.map((segment) => {
    if (segment.startSecs >= time - TIME_EPSILON && segment.included) {
      changed = true;
      return { ...segment, included: false };
    }
    return { ...segment };
  });

  return changed || split !== project ? { ...split, segments } : project;
};

export const getIncludedRanges = (project: VideoEditProject): VideoEditRange[] => {
  const included = project.segments
    .filter((segment) => segment.included && segment.endSecs > segment.startSecs)
    .sort((left, right) => left.startSecs - right.startSecs);

  return included.reduce<VideoEditRange[]>((ranges, segment) => {
    const previous = ranges[ranges.length - 1];
    if (previous && segment.startSecs <= previous.endSecs + TIME_EPSILON) {
      previous.endSecs = Math.max(previous.endSecs, segment.endSecs);
      return ranges;
    }

    ranges.push({ startSecs: segment.startSecs, endSecs: segment.endSecs });
    return ranges;
  }, []);
};

export const stepFrame = (
  playheadSecs: number,
  frameDelta: number,
  fps: number,
  durationSecs: number,
): number => {
  const duration = finiteNonNegative(durationSecs);
  const current = clamp(finiteNonNegative(playheadSecs), 0, duration);
  if (!Number.isFinite(fps) || fps <= 0 || !Number.isFinite(frameDelta)) return current;

  return clamp(current + frameDelta / fps, 0, duration);
};

export const createEditHistory = (project: VideoEditProject): VideoEditHistory => ({
  past: [],
  present: cloneVideoEditProject(project),
  future: [],
});

export const commitEdit = (
  history: VideoEditHistory,
  project: VideoEditProject,
): VideoEditHistory => ({
  past: [...history.past.map(cloneVideoEditProject), cloneVideoEditProject(history.present)],
  present: cloneVideoEditProject(project),
  future: [],
});

export const undoEdit = (history: VideoEditHistory): VideoEditHistory => {
  if (history.past.length === 0) return history;

  const previous = history.past[history.past.length - 1];
  return {
    past: history.past.slice(0, -1).map(cloneVideoEditProject),
    present: cloneVideoEditProject(previous),
    future: [cloneVideoEditProject(history.present), ...history.future.map(cloneVideoEditProject)],
  };
};

export const redoEdit = (history: VideoEditHistory): VideoEditHistory => {
  if (history.future.length === 0) return history;

  const next = history.future[0];
  return {
    past: [...history.past.map(cloneVideoEditProject), cloneVideoEditProject(history.present)],
    present: cloneVideoEditProject(next),
    future: history.future.slice(1).map(cloneVideoEditProject),
  };
};

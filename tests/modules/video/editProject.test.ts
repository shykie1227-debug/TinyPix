import { describe, expect, it } from 'vitest';
import {
  commitEdit,
  createEditHistory,
  createVideoEditProject,
  getIncludedRanges,
  redoEdit,
  setSegmentIncluded,
  splitAtPlayhead,
  stepFrame,
  trimToInPoint,
  trimToOutPoint,
  undoEdit,
} from '../../../src/modules/video/editProject';

const makeProject = () =>
  createVideoEditProject({
    inputPath: 'C:\\媒体\\source.mp4',
    durationSecs: 12,
    fps: 25,
    outputPath: 'C:\\媒体\\source-edited.mp4',
  });

describe('video edit project', () => {
  it('starts with one complete included segment', () => {
    expect(makeProject()).toEqual({
      inputPath: 'C:\\媒体\\source.mp4',
      durationSecs: 12,
      fps: 25,
      segments: [{ id: 'segment-1', startSecs: 0, endSecs: 12, included: true }],
      mode: 'lossless',
      outputMode: 'merge',
      outputPath: 'C:\\媒体\\source-edited.mp4',
    });
  });

  it('splits the segment under the playhead without mutating the project', () => {
    const project = makeProject();
    const next = splitAtPlayhead(project, 4.5);

    expect(project.segments).toEqual([
      { id: 'segment-1', startSecs: 0, endSecs: 12, included: true },
    ]);
    expect(next.segments).toEqual([
      { id: 'segment-1', startSecs: 0, endSecs: 4.5, included: true },
      { id: 'segment-2', startSecs: 4.5, endSecs: 12, included: true },
    ]);
    expect(splitAtPlayhead(next, 4.5)).toBe(next);
  });

  it('deletes and restores a selected segment non-destructively', () => {
    const split = splitAtPlayhead(splitAtPlayhead(makeProject(), 3), 9);
    const deleted = setSegmentIncluded(split, 'segment-2', false);

    expect(deleted.segments[1]).toEqual({
      id: 'segment-2',
      startSecs: 3,
      endSecs: 9,
      included: false,
    });
    expect(setSegmentIncluded(deleted, 'segment-2', true).segments[1].included).toBe(true);
    expect(split.segments[1].included).toBe(true);
  });

  it('sets I and O boundaries by splitting and excluding material outside them', () => {
    const withIn = trimToInPoint(makeProject(), 2.5);
    expect(withIn.segments).toEqual([
      { id: 'segment-1', startSecs: 0, endSecs: 2.5, included: false },
      { id: 'segment-2', startSecs: 2.5, endSecs: 12, included: true },
    ]);

    const withOut = trimToOutPoint(withIn, 8.25);
    expect(withOut.segments).toEqual([
      { id: 'segment-1', startSecs: 0, endSecs: 2.5, included: false },
      { id: 'segment-2', startSecs: 2.5, endSecs: 8.25, included: true },
      { id: 'segment-3', startSecs: 8.25, endSecs: 12, included: false },
    ]);
  });

  it('keeps immutable snapshots for undo and redo', () => {
    const initial = makeProject();
    const split = splitAtPlayhead(initial, 5);
    const history = commitEdit(createEditHistory(initial), split);
    const undone = undoEdit(history);

    expect(undone.present).toEqual(initial);
    expect(undone.present).not.toBe(initial);
    expect(undone.future[0]).toEqual(split);

    const redone = redoEdit(undone);
    expect(redone.present).toEqual(split);
    expect(redone.present).not.toBe(split);
    expect(initial.segments).toHaveLength(1);
  });

  it('merges adjacent included segments into export ranges and skips deleted ones', () => {
    const split = splitAtPlayhead(splitAtPlayhead(makeProject(), 3), 9);
    expect(getIncludedRanges(split)).toEqual([{ startSecs: 0, endSecs: 12 }]);

    const middleDeleted = setSegmentIncluded(split, 'segment-2', false);
    expect(getIncludedRanges(middleDeleted)).toEqual([
      { startSecs: 0, endSecs: 3 },
      { startSecs: 9, endSecs: 12 },
    ]);
  });

  it('steps by frames and clamps the playhead to the project duration', () => {
    expect(stepFrame(2, 1, 25, 12)).toBeCloseTo(2.04);
    expect(stepFrame(2, -1, 25, 12)).toBeCloseTo(1.96);
    expect(stepFrame(11.99, 1, 25, 12)).toBe(12);
    expect(stepFrame(0.01, -1, 25, 12)).toBe(0);
    expect(stepFrame(3, 1, 0, 12)).toBe(3);
  });
});

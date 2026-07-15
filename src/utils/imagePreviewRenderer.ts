import type { ColorAdjust, CropPercent } from '../stores/appStore';

export const buildCanvasFilter = (color: ColorAdjust) =>
  `brightness(${100 + color.brightness}%) contrast(${100 + color.contrast}%) saturate(${100 + color.saturation}%)`;

export const previewGeometry = (
  sourceWidth: number,
  sourceHeight: number,
  rotation: number,
  exactWidth?: number,
  exactHeight?: number
) => {
  if (exactWidth && exactHeight) {
    return { width: exactWidth, height: exactHeight };
  }
  const quarterTurn = Math.abs(rotation) % 180 === 90;
  return quarterTurn
    ? { width: sourceHeight, height: sourceWidth }
    : { width: sourceWidth, height: sourceHeight };
};

type Point = { x: number; y: number };

const normalizedRotation = (rotation: number) => ((Math.round(rotation / 90) * 90) % 360 + 360) % 360;
const cleanPercent = (value: number) => Math.round(value * 1_000_000) / 1_000_000;

const rectangleFromPoints = (points: Point[]): CropPercent => {
  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  const x = Math.min(...xs);
  const y = Math.min(...ys);
  return {
    x: cleanPercent(x * 100),
    y: cleanPercent(y * 100),
    width: cleanPercent((Math.max(...xs) - x) * 100),
    height: cleanPercent((Math.max(...ys) - y) * 100),
  };
};

const cropCorners = (crop: CropPercent): Point[] => {
  const left = crop.x / 100;
  const top = crop.y / 100;
  const right = (crop.x + crop.width) / 100;
  const bottom = (crop.y + crop.height) / 100;
  return [
    { x: left, y: top },
    { x: right, y: top },
    { x: left, y: bottom },
    { x: right, y: bottom },
  ];
};

const sourcePointToDisplay = (
  point: Point,
  rotation: number,
  flipH: boolean,
  flipV: boolean
): Point => {
  const x = flipH ? 1 - point.x : point.x;
  const y = flipV ? 1 - point.y : point.y;
  switch (normalizedRotation(rotation)) {
    case 90:
      return { x: 1 - y, y: x };
    case 180:
      return { x: 1 - x, y: 1 - y };
    case 270:
      return { x: y, y: 1 - x };
    default:
      return { x, y };
  }
};

const displayPointToSource = (
  point: Point,
  rotation: number,
  flipH: boolean,
  flipV: boolean
): Point => {
  let x: number;
  let y: number;
  switch (normalizedRotation(rotation)) {
    case 90:
      x = point.y;
      y = 1 - point.x;
      break;
    case 180:
      x = 1 - point.x;
      y = 1 - point.y;
      break;
    case 270:
      x = 1 - point.y;
      y = point.x;
      break;
    default:
      x = point.x;
      y = point.y;
  }
  return { x: flipH ? 1 - x : x, y: flipV ? 1 - y : y };
};

export const mapSourceCropToDisplay = (
  crop: CropPercent,
  rotation: number,
  flipH: boolean,
  flipV: boolean
) => rectangleFromPoints(
  cropCorners(crop).map((point) => sourcePointToDisplay(point, rotation, flipH, flipV))
);

export const mapDisplayCropToSource = (
  crop: CropPercent,
  rotation: number,
  flipH: boolean,
  flipV: boolean
) => rectangleFromPoints(
  cropCorners(crop).map((point) => displayPointToSource(point, rotation, flipH, flipV))
);

const clampByte = (value: number) => Math.max(0, Math.min(255, Math.round(value)));

export const applySharpen = (
  source: Uint8ClampedArray,
  width: number,
  height: number,
  amountPercent: number
) => {
  const output = new Uint8ClampedArray(source);
  const strength = Math.max(0, Math.min(100, amountPercent)) / 100;
  if (strength === 0 || width < 3 || height < 3) return output;

  const offset = (x: number, y: number) => (y * width + x) * 4;
  for (let y = 1; y < height - 1; y += 1) {
    for (let x = 1; x < width - 1; x += 1) {
      const center = offset(x, y);
      const neighbors = [offset(x - 1, y), offset(x + 1, y), offset(x, y - 1), offset(x, y + 1)];
      for (let channel = 0; channel < 3; channel += 1) {
        const neighborSum = neighbors.reduce((sum, index) => sum + source[index + channel], 0);
        output[center + channel] = clampByte(
          source[center + channel] * (1 + 4 * strength) - neighborSum * strength
        );
      }
      output[center + 3] = source[center + 3];
    }
  }
  return output;
};

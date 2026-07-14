export const getExtension = (path: string, fallback = 'mp4') => {
  const name = path.split(/[\\/]/).pop() || path;
  const match = name.match(/\.([^.]+)$/);
  return (match?.[1] || fallback).toLowerCase();
};

const basenameWithoutExtension = (path: string) => {
  const name = path.split(/[\\/]/).pop() || path;
  return name.replace(/\.[^.]+$/, '');
};

export const withVideoSuffix = (
  path: string,
  suffix: string,
  extension?: string,
  outputDir?: string
) => {
  const ext = (extension || getExtension(path)).replace(/^\./, '').toLowerCase();
  const dotExt = `.${ext}`;
  if (outputDir?.trim()) {
    const dir = outputDir.replace(/[\\/]+$/, '');
    const separator = dir.includes('\\') && !dir.includes('/') ? '\\' : '/';
    return `${dir}${separator}${basenameWithoutExtension(path)}${suffix}${dotExt}`;
  }
  if (/\.[^./\\]+$/.test(path)) {
    return path.replace(/\.[^./\\]+$/, `${suffix}${dotExt}`);
  }
  return `${path}${suffix}${dotExt}`;
};

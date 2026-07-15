import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const baseUrl = process.env.TINYPIX_AUDIT_URL || 'http://127.0.0.1:4173';
const outputDir = resolve(process.env.TINYPIX_AUDIT_OUTPUT || 'artifacts/ui-validation');
const viewports = [
  { width: 900, height: 520 },
  { width: 900, height: 600 },
  { width: 1080, height: 640 },
  { width: 1200, height: 800 },
];
const pages = [
  { id: 'video-output', tab: '视频工具', tool: '视频输出' },
  { id: 'gif-maker', tab: '视频工具', tool: 'GIF 制作' },
  { id: 'video-trimmer', tab: '视频工具', tool: '视频剪辑' },
  { id: 'image-workbench', tab: '图片工具', tool: '图片处理' },
];

await mkdir(outputDir, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  channel: process.env.TINYPIX_AUDIT_BROWSER_CHANNEL || 'chrome',
  args: ['--disable-gpu'],
});
const results = [];

try {
  for (const viewport of viewports) {
    for (const target of pages) {
      const page = await browser.newPage({ viewport });
      const pageErrors = [];
      page.on('pageerror', (error) => pageErrors.push(error.message));
      await page.goto(baseUrl, { waitUntil: 'networkidle' });
      await page.getByRole('button', { name: target.tab, exact: true }).click();
      await page.getByRole('button', { name: target.tool, exact: true }).click();
      await page.getByText('加载中…', { exact: true }).waitFor({ state: 'hidden', timeout: 5000 }).catch(() => {});
      await page.waitForTimeout(500);

      const metrics = await page.evaluate(() => {
        const root = document.documentElement;
        const body = document.body;
        const status = document.querySelector('[data-testid="status-bar"]');
        const workbench = document.querySelector('main');
        const statusRect = status?.getBoundingClientRect();
        const workbenchRect = workbench?.getBoundingClientRect();
        return {
          documentWidth: root.scrollWidth,
          viewportWidth: root.clientWidth,
          bodyWidth: body.scrollWidth,
          horizontalOverflow: root.scrollWidth > root.clientWidth + 1 || body.scrollWidth > root.clientWidth + 1,
          statusBottom: statusRect?.bottom ?? null,
          statusHeight: statusRect?.height ?? null,
          workbenchBottom: workbenchRect?.bottom ?? null,
          viewportHeight: root.clientHeight,
        };
      });

      const screenshot = `${viewport.width}x${viewport.height}-${target.id}.png`;
      await page.screenshot({ path: resolve(outputDir, screenshot), fullPage: false });
      results.push({ viewport, page: target.id, screenshot, metrics, pageErrors: [...pageErrors] });

      if (metrics.horizontalOverflow) {
        throw new Error(`${viewport.width}x${viewport.height} ${target.id} has horizontal overflow`);
      }
      if (metrics.statusBottom === null || Math.abs(metrics.statusBottom - metrics.viewportHeight) > 1) {
        throw new Error(`${viewport.width}x${viewport.height} ${target.id} status bar is not anchored in the app grid`);
      }
      if (metrics.statusHeight !== 48) {
        throw new Error(`${viewport.width}x${viewport.height} ${target.id} status bar height is ${metrics.statusHeight}`);
      }
      await page.close();
    }
  }
} finally {
  await writeFile(resolve(outputDir, 'report.json'), `${JSON.stringify(results, null, 2)}\n`, 'utf8');
  await browser.close();
}

console.log(`TinyPix visual audit passed: ${results.length} page/viewport combinations`);

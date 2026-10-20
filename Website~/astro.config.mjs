import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightImageZoom from 'starlight-image-zoom';
import starlightLinksValidator from 'starlight-links-validator';
import site from './src/data/site.json' with { type: 'json' };
import locales from './src/data/locales.json' with { type: 'json' };
import { unified } from '@astrojs/markdown-remark';
import { headingIds } from './src/plugins/heading-ids.mjs';

export default defineConfig({
  site: site.site,
  base: site.base,
  trailingSlash: 'always',
  markdown: { processor: unified({ smartypants: false, remarkPlugins: [headingIds] }) },
  integrations: [
    starlight({
      title: 'AvatarDoc',
      description: 'Toggles, radials, submenus and triggers for VRChat avatars, built at upload through NDMF.',
      logo: { src: './src/assets/logo.png', alt: 'AvatarDoc' },
      favicon: '/favicon.png',
      social: [{ icon: 'github', label: 'GitHub', href: site.githubRepo }],
      editLink: { baseUrl: site.githubRepo + '/edit/main/Website~/' },
      lastUpdated: true,
      defaultLocale: 'root',
      locales: Object.fromEntries(Object.entries(locales).map(([key, { label, lang, dir }]) => [key, dir ? { label, lang, dir } : { label, lang }])),
      components: { Banner: './src/components/Banner.astro', Footer: './src/components/Footer.astro' },
      customCss: ['./src/styles/custom.css'],
      plugins: [starlightImageZoom(), starlightLinksValidator({ errorOnRelativeLinks: false })],
      sidebar: [
        { label: 'Start here', translations: { ja: 'はじめに', ar: 'ابدأ هنا' }, items: [{ autogenerate: { directory: 'start-here' } }] },
        { label: 'Guides', translations: { ja: 'ガイド', ar: 'الأدلة' }, items: [{ autogenerate: { directory: 'guides' } }] },
        {
          label: 'Reference',
          translations: { ja: 'リファレンス', ar: 'المرجع' },
          items: [
            'reference/inspector',
            'reference/toggle',
            'reference/radial',
            'reference/submenu',
            'reference/exclusive-groups',
            'reference/triggers',
            'reference/touch-triggers',
            'reference/blend-shape-triggers',
            'reference/blend-shapes',
            'reference/material-properties',
            'reference/material-slots',
            'reference/objects',
            'reference/overrides',
            'reference/merged-animators',
            'reference/menu-assets-and-parameters',
            'reference/presets',
            'reference/grabbable',
            'reference/outfit-sets',
            'reference/avatardoc-shapes',
            'reference/convert-window',
            'reference/import-window',
            'reference/icon-capture',
            'reference/window-and-wheel',
            'reference/scene-preview',
            'reference/summary-and-lock',
            'reference/languages',
            'reference/limits',
            'reference/messages',
          ],
        },
        { label: 'How it works', translations: { ja: 'しくみ', ar: 'كيف يعمل' }, items: [{ autogenerate: { directory: 'how-it-works' } }] },
        { label: 'Help', translations: { ja: 'ヘルプ', ar: 'المساعدة' }, items: [{ autogenerate: { directory: 'help' } }] },
      ],
    }),
  ],
});

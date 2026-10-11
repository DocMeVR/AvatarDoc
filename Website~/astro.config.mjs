import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightImageZoom from 'starlight-image-zoom';
import starlightLinksValidator from 'starlight-links-validator';
import site from './src/data/site.json' with { type: 'json' };

export default defineConfig({
  site: site.site,
  base: site.base,
  trailingSlash: 'always',
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
      locales: { root: { label: 'English', lang: 'en' } },
      customCss: ['./src/styles/custom.css'],
      plugins: [starlightImageZoom(), starlightLinksValidator({ errorOnRelativeLinks: false })],
      sidebar: [
        { label: 'Start here', items: ['start-here/install'] },
      ],
    }),
  ],
});

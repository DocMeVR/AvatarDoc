import { defineCollection } from 'astro:content';
import { z } from 'astro/zod';
import { docsLoader, i18nLoader } from '@astrojs/starlight/loaders';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: z.object({
        sourceHash: z
          .string()
          .regex(/^[0-9a-f]{64}$/, 'sourceHash is the sha256 of the English page: run npm run source-hash -- <this page>')
          .optional(),
      }),
    }),
  }),
  i18n: defineCollection({
    loader: i18nLoader(),
    schema: i18nSchema({
      extend: z
        .object({
          'avatardoc.translation.notice': z.string(),
          'avatardoc.translation.outdated': z.string(),
        })
        .partial(),
    }),
  }),
};

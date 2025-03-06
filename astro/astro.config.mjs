// @ts-check
import { defineConfig, envField } from 'astro/config';

// https://astro.build/config
export default defineConfig(
{
	env : 
	{
		schema : 
		{
			SOCIAL_SECURITY_NUMBER: envField.string({ context : "server", access : "secret" }),
			DATA_ENCRYPTION_PASSWORD: envField.string({ context : "server", access : "secret" }),
		}
	}
});

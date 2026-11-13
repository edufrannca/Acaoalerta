import { defineConfig } from '@playwright/test'

/**
 * E2E contra a aplicação completa (API + PWA + centrais simuladas) já em execução.
 * Suba antes: ./scripts/executar-local.sh (ou docker compose up). URL: E2E_URL (padrão http://localhost:8080).
 * Navegador: CHROME_PATH (Chrome instalado) ou `npx playwright install chromium`.
 */
export default defineConfig({
  testDir: './testes',
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_URL ?? 'http://localhost:8080',
    locale: 'pt-BR',
    timezoneId: 'America/Recife',
    permissions: ['geolocation'],
    geolocation: { latitude: -8.1262, longitude: -34.9478, accuracy: 15 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    launchOptions: process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] } : {},
  },
})

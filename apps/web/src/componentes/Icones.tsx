/** Conjunto único de ícones (traço 2px, cantos arredondados), desenhado para o Alerta em Ação. */
type P = { tamanho?: number; className?: string }
const svg = (d: React.ReactNode, { tamanho = 24, className = '' }: P) => (
  <svg width={tamanho} height={tamanho} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className} aria-hidden="true">{d}</svg>
)
export const IcTelefone = (p: P) => svg(<path d="M5 3h3l2 5-2.5 1.5a11 11 0 0 0 7 7L16 14l5 2v3a2 2 0 0 1-2 2A17 17 0 0 1 3 5a2 2 0 0 1 2-2z" />, p)
export const IcSeguro = (p: P) => svg(<><circle cx="12" cy="12" r="9" /><path d="m8 12.5 2.8 2.7L16.5 9.5" /></>, p)
export const IcAjudar = (p: P) => svg(<><circle cx="9" cy="8" r="3" /><path d="M3.5 19a5.5 5.5 0 0 1 11 0" /><circle cx="17" cy="9" r="2.4" /><path d="M15.5 14.2A4.5 4.5 0 0 1 21 18.5" /></>, p)
export const IcLocal = (p: P) => svg(<><path d="M12 21s-6.5-5.6-6.5-11a6.5 6.5 0 0 1 13 0C18.5 15.4 12 21 12 21z" /><circle cx="12" cy="10" r="2.4" /></>, p)
export const IcChuva = (p: P) => svg(<><path d="M7 15a4.5 4.5 0 1 1 1.2-8.8A5.5 5.5 0 0 1 18.6 8 3.6 3.6 0 0 1 18 15H7z" /><path d="m8.5 18-1 2.5M12.5 18l-1 2.5M16.5 18l-1 2.5" /></>, p)
export const IcAlerta = (p: P) => svg(<><path d="M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z" /><path d="M12 9.5v4M12 17h.01" /></>, p)
export const IcAbrigo = (p: P) => svg(<><path d="M3.5 11 12 4l8.5 7" /><path d="M5.5 9.5V20h13V9.5" /><path d="M10 20v-5h4v5" /></>, p)
export const IcMapa = (p: P) => svg(<><path d="M9 4 3.5 6v14L9 18l6 2 5.5-2V4L15 6 9 4z" /><path d="M9 4v14M15 6v14" /></>, p)
export const IcSino = (p: P) => svg(<><path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15L6 16z" /><path d="M10 20.5a2 2 0 0 0 4 0" /></>, p)
export const IcPessoa = (p: P) => svg(<><circle cx="12" cy="8" r="4" /><path d="M4.5 21a7.5 7.5 0 0 1 15 0" /></>, p)
export const IcInicio = (p: P) => svg(<><path d="M3.5 11 12 4l8.5 7" /><path d="M5.5 9.5V20h5v-5h3v5h5V9.5" /></>, p)
export const IcSeta = (p: P) => svg(<path d="m9 5 7 7-7 7" />, p)
export const IcVoltar = (p: P) => svg(<path d="M19 12H5M11 6l-6 6 6 6" />, p)
export const IcRecentrar = (p: P) => svg(<path d="M20.5 3.5 3.5 10.6l7 2.9 2.9 7 7.1-17z" />, p)
export const IcCamadas = (p: P) => svg(<><path d="m12 3.5 9 4.8-9 4.8-9-4.8 9-4.8z" /><path d="m3 12.5 9 4.8 9-4.8M3 16.7l9 4.8 9-4.8" /></>, p)
export const IcMais = (p: P) => svg(<path d="M12 5v14M5 12h14" />, p)
export const IcMenos = (p: P) => svg(<path d="M5 12h14" />, p)
export const IcRelogio = (p: P) => svg(<><circle cx="12" cy="12" r="9" /><path d="M12 7.5V12l3 2" /></>, p)

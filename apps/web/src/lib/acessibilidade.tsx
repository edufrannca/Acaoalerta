import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

/** Preferências de acessibilidade por aparelho (armazenamento local; falha silenciosa se indisponível). */
import type { PreferenciaVoz } from './fala'

/** som: sinal sonoro curto quando a atenção a alagamentos sobe (desligado por padrão); vibrar: vibração onde o aparelho suporta. */
export interface PrefsAcessibilidade { escala: number; altoContraste: boolean; velocidadeVoz: number; voz: PreferenciaVoz; som: boolean; vibrar: boolean }
const PADRAO: PrefsAcessibilidade = { escala: 1, altoContraste: false, velocidadeVoz: 1, voz: 'auto', som: false, vibrar: false }
const CHAVE = 'alerta.acessibilidade'
export const ESCALAS = [1, 1.15, 1.3, 1.5]

function ler(): PrefsAcessibilidade {
  try { return { ...PADRAO, ...JSON.parse(localStorage.getItem(CHAVE) ?? '{}') } } catch { return PADRAO }
}

const Ctx = createContext<{ prefs: PrefsAcessibilidade; alterar: (p: Partial<PrefsAcessibilidade>) => void }>({ prefs: PADRAO, alterar: () => {} })

export function ProvedorAcessibilidade({ children }: { children: ReactNode }) {
  const [prefs, setPrefs] = useState<PrefsAcessibilidade>(ler)
  useEffect(() => {
    const html = document.documentElement
    // Os tamanhos do layout são em px; o zoom do documento amplia texto, alvos de toque e espaçamentos juntos.
    html.style.setProperty('zoom', String(prefs.escala))
    html.dataset.contraste = prefs.altoContraste ? 'alto' : 'normal'
    try { localStorage.setItem(CHAVE, JSON.stringify(prefs)) } catch { /* modo privado */ }
  }, [prefs])
  return <Ctx.Provider value={{ prefs, alterar: (p) => setPrefs((a) => ({ ...a, ...p })) }}>{children}</Ctx.Provider>
}

export const useAcessibilidade = () => useContext(Ctx)

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, definirToken, obterToken } from './api'

export interface Usuario {
  id: string
  nome: string
  email: string
  papel: 'Cidadao' | 'Voluntario' | 'OperadorCentral' | 'Organizacao' | 'Administrador'
  centralCodigo?: string | null
  municipioCodigo?: string | null
  enderecoReferencia?: string | null
  consenteAlertas: boolean
  consenteLocalizacao: boolean
  receberPush: boolean
  contaDemonstracao: boolean
}

interface Sessao {
  usuario: Usuario | null
  carregando: boolean
  entrar: (email: string, senha: string) => Promise<void>
  entrarDemo: (perfil: string) => Promise<void>
  registrar: (d: { nome: string; email: string; senha: string; municipioCodigo?: string; telefone?: string }) => Promise<void>
  sair: () => void
  recarregar: () => Promise<void>
}

const Ctx = createContext<Sessao | null>(null)

export function ProvedorSessao({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<Usuario | null>(null)
  const [carregando, setCarregando] = useState(true)

  const recarregar = useCallback(async () => {
    if (!obterToken()) { setUsuario(null); setCarregando(false); return }
    try { setUsuario(await api<Usuario>('/api/eu/')) } catch { setUsuario(null) } finally { setCarregando(false) }
  }, [])

  useEffect(() => { void recarregar() }, [recarregar])

  const aplicar = async (r: { accessToken: string }) => { definirToken(r.accessToken); await recarregar() }

  const valor: Sessao = {
    usuario, carregando, recarregar,
    entrar: async (email, senha) => aplicar(await api('/api/auth/entrar', { method: 'POST', json: { email, senha } })),
    entrarDemo: async (perfil) => {
      let codigo = ''
      try { codigo = sessionStorage.getItem('alerta.codigoDemo') ?? '' } catch { /* modo privado */ }
      await aplicar(await api('/api/auth/demo', { method: 'POST', json: { perfil }, headers: { 'X-Demo-Codigo': codigo } }))
    },
    registrar: async (d) => aplicar(await api('/api/auth/registrar', { method: 'POST', json: d })),
    sair: () => { definirToken(null); setUsuario(null) },
  }
  return <Ctx.Provider value={valor}>{children}</Ctx.Provider>
}

export function useSessao() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useSessao fora do provedor')
  return c
}

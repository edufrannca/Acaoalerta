import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, Link, useLocation } from 'react-router'
import { observarFila } from '../lib/filaOffline'
import type { ItemFila } from '../lib/dispositivo'
import { useOnline } from '../lib/hooks'
import { useSessao } from '../lib/sessao'
import { instalarParadaAutomatica, parar } from '../lib/fala'
import { Marca } from './Base'
import { EVENTO_NOVA_VERSAO } from '../lib/versao'
import { BotaoAcessibilidade } from './PainelAcessibilidade'
import { IcAbrigo, IcInicio, IcMapa, IcPessoa, IcSino } from './Icones'

instalarParadaAutomatica()

const ITENS = [
  { para: '/', rotulo: 'Início', Icone: IcInicio },
  { para: '/mapa', rotulo: 'Mapa', Icone: IcMapa },
  { para: '/alertas', rotulo: 'Alertas', Icone: IcSino },
  { para: '/abrigos', rotulo: 'Abrigos', Icone: IcAbrigo },
  { para: '/perfil', rotulo: 'Perfil', Icone: IcPessoa },
]

function NavInferior() {
  return (
    <nav aria-label="Navegação principal" className="fixed inset-x-0 bottom-0 z-30 border-t border-linha bg-branco pb-[env(safe-area-inset-bottom)] md:hidden">
      <ul className="mx-auto grid max-w-lg grid-cols-5">
        {ITENS.map(({ para, rotulo, Icone }) => (
          <li key={para}>
            <NavLink to={para} end={para === '/'} className={({ isActive }) =>
              `relative flex min-h-16 flex-col items-center justify-center gap-0.5 text-[12.5px] font-semibold ${isActive ? 'text-oceano' : 'text-tinta-suave'}`}>
              {({ isActive }) => (
                <>
                  <Icone tamanho={25} />
                  {rotulo}
                  {isActive && <span className="absolute bottom-1 h-[3px] w-10 rounded-full bg-oceano" aria-hidden="true" />}
                </>
              )}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}

function NavTopo() {
  const { usuario } = useSessao()
  const operador = usuario && ['OperadorCentral', 'Administrador', 'Organizacao'].includes(usuario.papel)
  const itens = [...ITENS.slice(0, 4), ...(operador ? [{ para: '/painel', rotulo: 'Painel', Icone: IcMapa }] : []), ITENS[4]]
  return (
    <nav aria-label="Navegação principal" className="hidden md:block">
      <ul className="flex items-center gap-1">
        {itens.map(({ para, rotulo, Icone }) => (
          <li key={para}>
            <NavLink to={para} end={para === '/'} className={({ isActive }) =>
              `flex min-h-11 items-center gap-2 rounded-controle px-3 text-[15px] font-semibold ${isActive ? 'bg-oceano-suave text-oceano' : 'text-tinta-suave hover:bg-fundo hover:text-petroleo'}`}>
              <Icone tamanho={20} />{rotulo}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}

export function Estrutura({ children }: { children: ReactNode }) {
  const online = useOnline()
  const [novaVersao, setNovaVersao] = useState(false)
  useEffect(() => {
    const f = () => setNovaVersao(true)
    window.addEventListener(EVENTO_NOVA_VERSAO, f)
    return () => window.removeEventListener(EVENTO_NOVA_VERSAO, f)
  }, [])
  const { pathname } = useLocation()
  const [pendentes, setPendentes] = useState(0)
  useEffect(() => observarFila((l: ItemFila[]) => setPendentes(l.filter((x) => x.estado === 'salvo_no_aparelho' || x.estado === 'transmitindo').length)), [])
  useEffect(() => { parar() }, [pathname]) // a leitura por voz não continua ao trocar de página
  const telaMapa = pathname === '/mapa'

  return (
    <div className={`min-h-dvh ${telaMapa ? '' : 'pb-24 md:pb-10'}`}>
      <a href="#conteudo" className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-branco focus:px-3 focus:py-2">Pular para o conteúdo</a>
      <header className={`border-b border-linha bg-branco ${telaMapa ? 'hidden md:block' : ''}`}>
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 pb-3 pt-[max(0.75rem,env(safe-area-inset-top))]">
          <Link to="/" className="flex min-w-0 items-center gap-3" aria-label="Alerta em Ação, início">
            <Marca tamanho={42} />
            <span className="min-w-0 leading-tight">
              <span className="block text-[23px] font-bold tracking-tight text-petroleo">Alerta em Ação</span>
              <span className="block truncate text-[13.5px] text-tinta-suave">Emergência e riscos climáticos na sua região</span>
            </span>
          </Link>
          <div className="flex items-center gap-2">
            <NavTopo />
            {pendentes > 0 && (
              <Link to="/pedidos" className="hidden rounded-full bg-atencao-claro px-3 py-1.5 text-[13px] font-semibold text-atencao sm:inline-block">
                {pendentes} envio(s) no aparelho
              </Link>
            )}
            <BotaoAcessibilidade />
          </div>
        </div>
      </header>
      {novaVersao && (
        <div role="status" className="flex flex-wrap items-center justify-center gap-2 bg-oceano-suave px-4 py-2 text-[14px] font-medium text-petroleo">
          Há uma versão nova do app. Seus pedidos continuam salvos neste aparelho.
          <button type="button" onClick={() => location.reload()} className="min-h-11 rounded-controle bg-petroleo px-3 font-semibold text-branco">Atualizar agora</button>
        </div>
      )}
      {!online && (
        <div role="alert" className="bg-atencao-claro px-4 py-2 text-center text-[14px] font-medium text-atencao">
          Sem internet. Pedidos ficam salvos neste aparelho e são enviados quando a conexão voltar. Em emergência, ligue 193.
        </div>
      )}
      {pendentes > 0 && !telaMapa && (
        <Link to="/pedidos" className="block bg-atencao-claro px-4 py-1.5 text-center text-[13px] font-semibold text-atencao sm:hidden">
          {pendentes} envio(s) salvos neste aparelho — ver
        </Link>
      )}
      <main id="conteudo" className={telaMapa ? '' : 'mx-auto max-w-6xl px-4 pt-4 md:pt-6'}>{children}</main>
      {!telaMapa && (
        <footer className="mx-auto mt-8 max-w-6xl px-4 text-[12.5px] text-tinta-suave">
          Versão de demonstração do hackathon Devs Criam com IA 2026: as centrais de atendimento são simuladas e nenhum órgão real é acionado.
          {' '}<Link to="/instalar" className="font-semibold text-oceano underline">Instalar no celular</Link>
        </footer>
      )}
      <NavInferior />
    </div>
  )
}

import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, Link } from 'react-router'
import { observarFila } from '../lib/filaOffline'
import type { ItemFila } from '../lib/dispositivo'
import { useOnline } from '../lib/hooks'
import { useSessao } from '../lib/sessao'
import { Marca } from './Base'
import { BotaoAcessibilidade } from './PainelAcessibilidade'

function Navegacao({ escuro = false }: { escuro?: boolean }) {
  const { usuario } = useSessao()
  const operador = usuario && ['OperadorCentral', 'Administrador', 'Organizacao'].includes(usuario.papel)
  const itens = [
    { para: '/', rotulo: 'Início', icone: 'M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6h-6v6H4a1 1 0 0 1-1-1z' },
    { para: '/mapa', rotulo: 'Mapa', icone: 'M9 4l6 2 6-2v16l-6 2-6-2-6 2V6zM9 4v16M15 6v16' },
    { para: '/pedidos', rotulo: 'Meus pedidos', icone: 'M5 4h14v16H5zM8 9h8M8 13h8M8 17h5' },
    ...(operador ? [{ para: '/painel', rotulo: 'Painel', icone: 'M4 4h7v7H4zM13 4h7v4h-7zM13 10h7v10h-7zM4 13h7v7H4z' }] : []),
    { para: '/perfil', rotulo: usuario ? 'Perfil' : 'Entrar', icone: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21a8 8 0 0 1 16 0' },
  ]
  return (
    <nav aria-label="Navegação principal" className="fixed inset-x-0 bottom-0 z-20 border-t border-linha bg-branco/95 pb-[env(safe-area-inset-bottom)] backdrop-blur md:static md:border-0 md:bg-transparent md:pb-0">
      <ul className="mx-auto flex max-w-3xl justify-around md:justify-end md:gap-1">
        {itens.map((i) => (
          <li key={i.para}>
            <NavLink to={i.para} end={i.para === '/'} className={({ isActive }) =>
              `flex min-w-16 flex-col items-center gap-0.5 px-2 py-2 text-[12px] font-medium md:flex-row md:gap-2 md:rounded-controle md:px-3 md:text-[14px] ${escuro
                ? (isActive ? 'bg-branco text-petroleo' : 'text-areia hover:bg-petroleo-900 hover:text-branco')
                : (isActive ? 'text-oceano md:bg-branco md:ring-1 md:ring-linha' : 'text-tinta-suave hover:text-petroleo')}`}>
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round" aria-hidden="true"><path d={i.icone} /></svg>
              {i.rotulo}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}

export function Estrutura({ children }: { children: ReactNode }) {
  const online = useOnline()
  const [pendentes, setPendentes] = useState(0)
  useEffect(() => observarFila((l: ItemFila[]) => setPendentes(l.filter((x) => x.estado === 'salvo_no_aparelho' || x.estado === 'transmitindo').length)), [])

  return (
    <div className="min-h-dvh pb-24 md:pb-8">
      <a href="#conteudo" className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-branco focus:px-3 focus:py-2">Pular para o conteúdo</a>
      <header className="bg-petroleo text-areia">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-3">
          <Link to="/" className="flex items-center gap-2.5" aria-label="Alerta em Ação, início">
            <Marca tamanho={34} />
            <span className="leading-tight">
              <span className="block text-[17px] font-bold tracking-tight text-branco">Alerta em Ação</span>
              <span className="block text-[12px] text-oceano-claro">Região Metropolitana do Recife</span>
            </span>
          </Link>
          <div className="flex items-center gap-2">
            <div className="hidden md:block"><Navegacao escuro /></div>
            <BotaoAcessibilidade />
          </div>
          <div role="status" aria-live="polite" className="text-right text-[12.5px] leading-tight md:hidden">
            <span className={`font-semibold ${online ? 'text-oceano-claro' : 'text-[#E3C07A]'}`}>{online ? 'Conectado' : 'Sem internet'}</span>
            {pendentes > 0 && <span className="block text-areia/80">{pendentes} envio(s) no aparelho</span>}
          </div>
        </div>
        <div className="linha-mare" data-estado={online ? 'online' : 'offline'} aria-hidden="true" />
      </header>
      {!online && (
        <div role="alert" className="bg-[#FBF1DC] px-4 py-2 text-center text-[14px] text-atencao">
          Você está sem internet. Pedidos ficam salvos neste aparelho e são enviados quando a conexão voltar. Em emergência, ligue 193.
        </div>
      )}
      <p className="bg-[#EDE9F3] px-4 py-1.5 text-center text-[12.5px] text-[#4A3A6B]">
        Ambiente de demonstração do hackathon: centrais de atendimento são simuladas e nenhum órgão real é acionado.{' '}
        <Link to="/instalar" className="font-semibold underline">Instalar no celular</Link>
      </p>
      <main id="conteudo" className="mx-auto max-w-5xl px-4 pt-4">{children}</main>
      <div className="md:hidden"><Navegacao /></div>
    </div>
  )
}

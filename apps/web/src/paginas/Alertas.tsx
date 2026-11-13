import { Link } from 'react-router'
import { Cartao, Fonte, Selo } from '../componentes/Base'
import { LeitorVoz } from '../componentes/LeitorVoz'
import { IcAlerta, IcSeta } from '../componentes/Icones'
import { formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'
import { useEvento } from '../lib/tempoReal'

interface Aviso { id: string; titulo: string; oficial: boolean; severidade: string; inicioEm: string; fimEm: string; municipios: string[]; instrucoes: string[]; riscos: string[]; fonteNome: string; fonteUrl: string | null; obtidoEm: string; poligono: unknown }
interface Notificacao { id: string; titulo: string; mensagem: string; pergunta: string; criadaEm: string; resposta: string | null; avisoOficial: boolean }

export function Alertas() {
  const { usuario } = useSessao()
  const avisos = useDados<Aviso[]>('/api/alertas/vigentes', { cache: true, intervaloMs: 60000 })
  const notifs = useDados<Notificacao[]>(usuario ? '/api/eu/notificacoes' : null, { intervaloMs: 30000 })
  useEvento('avisoPublicado', () => { void avisos.recarregar(); void notifs.recarregar() })
  const lista = avisos.dados ?? []
  return (
    <div className="mx-auto grid max-w-3xl gap-4">
      <h1 className="text-[26px] font-bold text-petroleo">Alertas</h1>
      <p className="text-[15px] text-tinta-suave">Avisos meteorológicos vigentes do INMET para a Região Metropolitana do Recife. Avisos demonstrativos aparecem identificados.</p>
      {avisos.doCache && <Selo tom="atencao">Mostrando cópia salva no aparelho</Selo>}
      {lista.length === 0 ? (
        <Cartao><p className="text-[15.5px]">Nenhum aviso vigente nas fontes consultadas agora. Isso não significa ausência de risco: em área de risco, siga as orientações da Defesa Civil (199).</p></Cartao>
      ) : lista.map((a) => (
        <section key={a.id} className={`rounded-cartao p-4 ring-1 sm:p-5 ${a.oficial ? 'border-l-[6px] border-emergencia bg-emergencia-claro/60 ring-emergencia/25' : 'border-l-[6px] border-[#6E5A95] bg-[#F3F0F8] ring-[#6E5A95]/25'}`}>
          <div className="flex items-start gap-3">
            <span className={a.oficial ? 'text-emergencia' : 'text-[#6E5A95]'}><IcAlerta tamanho={28} /></span>
            <div className="min-w-0 flex-1">
              <Selo tom={a.oficial ? 'erro' : 'demo'}>{a.oficial ? 'Aviso oficial' : 'Demonstrativo, não oficial'}</Selo>
              <h2 className="mt-1 text-[19px] font-bold text-petroleo">{a.titulo}</h2>
              <p className="text-[14.5px]">{a.severidade} · de {formatarHora(a.inicioEm)} até {formatarHora(a.fimEm)}</p>
              {a.municipios.length > 0 && <p className="text-[14px] text-tinta-suave">Municípios: {a.municipios.map(nomeMunicipio).join(', ')}</p>}
              {a.riscos?.length > 0 && <p className="mt-1 text-[14.5px]"><strong>Riscos:</strong> {a.riscos.join('; ')}</p>}
              {a.instrucoes.length > 0 && <ul className="mt-1 list-disc pl-5 text-[14.5px]">{a.instrucoes.slice(0, 6).map((i) => <li key={i}>{i}</li>)}</ul>}
              <Fonte nome={a.fonteNome} em={a.obtidoEm} oficial={a.oficial} demonstrativa={!a.oficial} />
              <LeitorVoz compacto rotulo="Ouvir aviso" texto={`${a.oficial ? 'Aviso oficial' : 'Aviso demonstrativo, não oficial'}. ${a.titulo}. ${a.severidade}. ${(a.riscos ?? []).join('. ')}. ${a.instrucoes.slice(0, 6).join('. ')}`} />
              {a.poligono != null && <Link to="/mapa" className="mt-2 inline-flex min-h-11 items-center gap-1 text-[15px] font-semibold text-oceano">Ver área no mapa <IcSeta tamanho={18} /></Link>}
            </div>
          </div>
        </section>
      ))}
      {usuario && (
        <Cartao titulo="Minhas notificações">
          {(notifs.dados ?? []).length === 0 ? <p className="text-[15px]">Nenhuma notificação para seus locais monitorados.</p> : (
            <ul className="grid gap-2">{notifs.dados!.slice(0, 10).map((n) => (
              <li key={n.id} className="rounded-controle bg-fundo p-3 text-[14.5px]">
                <p className="font-semibold text-petroleo">{n.titulo}</p><p>{n.mensagem}</p>
                <p className="text-[13px] text-tinta-suave">{formatarHora(n.criadaEm)} · {n.resposta ? `Respondida: ${n.resposta}` : 'Sem resposta'}</p>
              </li>))}
            </ul>
          )}
        </Cartao>
      )}
      {!usuario && <p className="text-[14px] text-tinta-suave"><Link to="/perfil" className="font-semibold text-oceano underline">Entre ou crie uma conta</Link> para receber avisos dos locais que você escolher monitorar.</p>}
    </div>
  )
}

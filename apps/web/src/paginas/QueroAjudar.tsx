import { useState } from 'react'
import { CartaoPedido, FormOferta, ResumoRegiao } from '../componentes/PedidosComunidade'
import { usePedidosComunitarios, type PedidoComunitario } from '../lib/comunidade'
import { useLocalizacao } from '../lib/hooks'
import { Botao, Campo, Cartao, Fonte, Selo, Vazio, classeEntrada } from '../componentes/Base'
import { api, formatarHora } from '../lib/api'
import { uuid } from '../lib/dispositivo'
import { enfileirar } from '../lib/filaOffline'
import { useDados } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio, TIPOS_AJUDA } from '../lib/municipios'
import { useEvento } from '../lib/tempoReal'

interface Necessidade { id: string; organizacaoNome: string; municipioCodigo: string; tipo: string; titulo: string; descricao: string; pontoEntregaVerificado: string; ficticia: boolean }
interface Oferta { id: string; tipo: string; descricao: string; status: string; protocoloOrganizacao?: string | null; necessidadeId?: string | null; municipioCodigo?: string; historico: { em: string; descricao: string }[]; encaminhamento?: { status: string; protocolo: string | null; ultimoErro: string | null } | null; pedido?: string | null; instrucaoEquipe?: string | null }

const STATUS_OFERTA: Record<string, { rotulo: string; tom: 'ok' | 'espera' | 'erro' | 'neutro' }> = {
  Registrada: { rotulo: 'Registrada', tom: 'neutro' },
  EnviadaOrganizacao: { rotulo: 'Aguardando confirmação da organização', tom: 'espera' },
  RecebidaPelaOrganizacao: { rotulo: 'Recebida pela organização', tom: 'espera' },
  ConfirmadaPelaOrganizacao: { rotulo: 'Participação confirmada', tom: 'ok' },
  RecusadaPelaOrganizacao: { rotulo: 'Recusada pela organização', tom: 'erro' },
  Concluida: { rotulo: 'Concluída', tom: 'ok' },
  Cancelada: { rotulo: 'Cancelada', tom: 'neutro' },
  AguardandoAvaliacao: { rotulo: 'Oferta enviada · aguardando a equipe', tom: 'espera' },
  AceitaPelaEquipe: { rotulo: 'Aceita · aguardando orientações', tom: 'espera' },
  RecusadaPelaEquipe: { rotulo: 'Não foi necessária', tom: 'neutro' },
  Encaminhada: { rotulo: 'Orientações recebidas', tom: 'ok' },
}

export function QueroAjudar() {
  const [municipio, setMunicipio] = useState(() => localStorage.getItem('alerta.municipio') || '2611606')
  const necessidades = useDados<{ necessidades: Necessidade[]; observacao: string }>(`/api/apoio/necessidades?municipio=${municipio}`, { cache: true })
  const minhas = useDados<Oferta[]>('/api/apoio/ofertas/minhas', { intervaloMs: 8000 })
  useEvento('ofertaAtualizada', () => { void minhas.recarregar() })

  const [tipo, setTipo] = useState('DoacaoAgua')
  const [descricao, setDescricao] = useState('')
  const [disponibilidade, setDisponibilidade] = useState('')
  const [regiao, setRegiao] = useState('')
  const [contato, setContato] = useState('')
  const [autorizo, setAutorizo] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const registrar = async () => {
    const id = uuid()
    await enfileirar({ id, tipo: 'oferta', caminho: '/api/apoio/ofertas', corpo: { id, municipioCodigo: municipio, regiao: regiao || null, tipo, descricao, disponibilidade: disponibilidade || null, contatoAutorizado: autorizo ? contato || null : null } })
    setDescricao(''); setMsg('Oferta registrada. Agora escolha uma necessidade publicada para participar.')
    setTimeout(() => void minhas.recarregar(), 800)
  }

  const participar = async (ofertaId: string, necessidadeId: string) => {
    try { await api(`/api/apoio/ofertas/${ofertaId}/participacao`, { method: 'POST', json: { necessidadeId } }); setMsg('Pedido de participação enviado à organização.') }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Falha ao enviar') }
    void minhas.recarregar()
  }

  const { posicao, estado: gps, iniciar } = useLocalizacao(30000)
  const [raio, setRaio] = useState(5)
  const comunidade = usePedidosComunitarios(posicao, raio, posicao ? null : municipio)
  const [ofertando, setOfertando] = useState<PedidoComunitario | null>(null)
  const acompanhar = async (id: string, acao: 'concluida' | 'cancelada') => {
    try { await api(`/api/apoio/ofertas/${id}/acompanhamento`, { method: 'POST', json: { acao } }) } catch (e) { setMsg(e instanceof Error ? e.message : 'Falha ao atualizar') }
    void minhas.recarregar()
  }

  const livres = (minhas.dados ?? []).filter((o) => o.status === 'Registrada' || o.status === 'RecusadaPelaOrganizacao')

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div className="grid content-start gap-4">
        <h1 className="text-[26px] font-bold text-petroleo">Quero ajudar</h1>
        <p role="note" className="rounded-cartao bg-atencao-claro p-3 text-[14.5px]"><strong>Ajuda comunitária não é resgate.</strong> Nunca vá a áreas alagadas, de correnteza ou de deslizamento. Ofertas passam pela equipe responsável, que indica um ponto seguro.</p>
        <Cartao titulo={posicao ? 'Pedidos de ajuda perto de você' : 'Pedidos de ajuda compartilhados'}>
          <div className="mb-3 flex flex-wrap items-center gap-2 text-[14px]">
            {posicao ? (
              <label className="font-medium">Raio a partir da sua localização{' '}
                <select value={raio} onChange={(e) => setRaio(Number(e.target.value))} className="min-h-10 rounded-controle border border-linha bg-branco px-2">
                  {[2, 5, 10, 20].map((k) => <option key={k} value={k}>{k} km</option>)}
                </select>
              </label>
            ) : gps !== 'negado' ? <Botao variante="secundario" onClick={iniciar}>Buscar perto de mim</Botao> : <span className="text-tinta-suave">Localização negada: mostrando {nomeMunicipio(municipio)}.</span>}
          </div>
          {comunidade.dados && <ResumoRegiao r={comunidade.dados} />}
          {ofertando && <div className="mt-3"><FormOferta p={ofertando} aoFechar={() => setOfertando(null)} aoEnviar={(m) => { setMsg(m); setTimeout(() => void minhas.recarregar(), 800) }} /></div>}
          <div className="mt-3 grid gap-3">
            {(comunidade.dados?.pedidos ?? []).length === 0
              ? <Vazio>Nenhum pedido compartilhado {posicao ? `a até ${raio} km` : `em ${nomeMunicipio(municipio)}`} nas últimas 24 horas.</Vazio>
              : comunidade.dados!.pedidos.map((p) => <CartaoPedido key={p.id} p={p} aoOferecer={(x) => { setOfertando(x); setTimeout(() => document.getElementById('titulo-oferta')?.scrollIntoView({ behavior: 'smooth' }), 50) }} />)}
          </div>
          <p className="mt-3 text-[12.5px] text-tinta-suave">{comunidade.dados?.privacidade} {comunidade.dados?.areaAproximada}</p>
          {comunidade.dados?.demonstrativos && <p className="mt-1 text-[12.5px] font-semibold text-tinta-suave">Área de demonstração: exibindo pedidos FICTÍCIOS.</p>}
        </Cartao>
        <Cartao titulo="Necessidades publicadas por organizações">
          <label className="mb-3 block">
            <span className="text-[14px] font-medium">Município</span>
            <select className={classeEntrada} value={municipio} onChange={(e) => setMunicipio(e.target.value)}>
              {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
            </select>
          </label>
          {(necessidades.dados?.necessidades ?? []).length === 0 ? <Vazio>Nenhuma necessidade aberta em {nomeMunicipio(municipio)}.</Vazio> : (
            <ul className="grid gap-3">
              {necessidades.dados!.necessidades.map((n) => (
                <li key={n.id} className="rounded-controle bg-areia p-3">
                  <div className="flex flex-wrap items-center gap-2"><Selo tom="neutro">{TIPOS_AJUDA[n.tipo] ?? n.tipo}</Selo>{n.ficticia && <Selo tom="demo">Fictícia</Selo>}</div>
                  <p className="mt-1 font-semibold text-petroleo">{n.titulo}</p>
                  <p className="text-[14px]">{n.descricao}</p>
                  <p className="mt-1 text-[13.5px]"><span className="font-medium">Entrega:</span> {n.pontoEntregaVerificado}</p>
                  <Fonte nome={n.organizacaoNome} demonstrativa={n.ficticia} />
                  {livres.length > 0 && (
                    <div className="mt-2 flex flex-wrap gap-2">
                      {livres.map((o) => <Botao key={o.id} variante="secundario" onClick={() => participar(o.id, n.id)}>Participar com: {o.descricao.slice(0, 28)}</Botao>)}
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
          <p className="mt-2 text-[12.5px] text-tinta-suave">{necessidades.dados?.observacao}</p>
        </Cartao>
      </div>

      <div className="grid content-start gap-4">
        <Cartao titulo="Registrar oferta">
          <div className="grid gap-3">
            <Campo rotulo="Tipo de ajuda">
              <select className={classeEntrada} value={tipo} onChange={(e) => setTipo(e.target.value)}>
                {Object.entries(TIPOS_AJUDA).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
              </select>
            </Campo>
            <Campo rotulo="O que você oferece"><input className={classeEntrada} value={descricao} onChange={(e) => setDescricao(e.target.value)} placeholder="Ex.: 12 garrafas de água de 1,5 L" maxLength={500} /></Campo>
            <Campo rotulo="Disponibilidade"><input className={classeEntrada} value={disponibilidade} onChange={(e) => setDisponibilidade(e.target.value)} placeholder="Ex.: sábado pela manhã" /></Campo>
            <Campo rotulo="Região onde pode ajudar"><input className={classeEntrada} value={regiao} onChange={(e) => setRegiao(e.target.value)} placeholder="Ex.: Boa Viagem" /></Campo>
            <Campo rotulo="Contato" dica="Compartilhado só com a organização escolhida, se você autorizar.">
              <input className={classeEntrada} value={contato} onChange={(e) => setContato(e.target.value)} placeholder="Telefone ou e-mail" />
            </Campo>
            <label className="flex items-start gap-2 text-[14px]"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={autorizo} onChange={(e) => setAutorizo(e.target.checked)} />Autorizo compartilhar meu contato com a organização</label>
            <Botao variante="primario" disabled={descricao.trim().length < 3} onClick={registrar}>Registrar oferta</Botao>
            {msg && <p role="status" className="text-[14px] text-seguro">{msg}</p>}
          </div>
        </Cartao>

        <Cartao titulo="Minhas ofertas">
          {(minhas.dados ?? []).length === 0 ? <Vazio>Você ainda não registrou ofertas neste aparelho.</Vazio> : (
            <ul className="grid gap-2">
              {minhas.dados!.map((o) => {
                const s = STATUS_OFERTA[o.status] ?? { rotulo: o.status, tom: 'neutro' as const }
                return (
                  <li key={o.id} className="rounded-controle bg-areia p-3">
                    <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{o.descricao}</span><Selo tom={s.tom}>{s.rotulo}</Selo></div>
                    {o.pedido && <p className="text-[13px] text-tinta-suave">Para o pedido {o.pedido}</p>}
                    {o.instrucaoEquipe && <p className="mt-1 rounded-controle bg-seguro-claro p-2 text-[14px]"><strong>Orientação da equipe:</strong> {o.instrucaoEquipe}</p>}
                    {o.protocoloOrganizacao && <p className="numero-tabular text-[13px]">Protocolo da organização simulada: {o.protocoloOrganizacao}</p>}
                    {o.pedido && ['AguardandoAvaliacao', 'AceitaPelaEquipe', 'Encaminhada'].includes(o.status) && (
                      <div className="mt-2 flex flex-wrap gap-2">
                        {o.status === 'Encaminhada' && <Botao variante="primario" onClick={() => acompanhar(o.id, 'concluida')}>Informar entrega concluída</Botao>}
                        <Botao variante="secundario" onClick={() => acompanhar(o.id, 'cancelada')}>Cancelar oferta</Botao>
                      </div>
                    )}
                    <ol className="mt-1 text-[12.5px] text-tinta-suave">{o.historico.slice(-3).map((h, i) => <li key={i}>{formatarHora(h.em)}: {h.descricao}</li>)}</ol>
                  </li>
                )
              })}
            </ul>
          )}
        </Cartao>
      </div>
    </div>
  )
}

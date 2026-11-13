import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { AvisoSimulado, Botao, Cartao, Selo, Vazio } from '../componentes/Base'
import { api, formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'
import { useEvento } from '../lib/tempoReal'

interface Recebimento {
  protocolo: string; tipoMensagem: string; identificadorReferencia: string; municipioCodigo: string; recebidoEm: string; estado: string; duplicatas: number
  mensagem: Record<string, unknown> & { relato?: string; latitude?: number; longitude?: number; tiposRisco?: string[]; descricao?: string; tipoAjuda?: string; contatoAutorizado?: string; sinalizacoes?: { titulo: string; nivel: string }[]; pessoasEnvolvidas?: { quantidade?: number; feridos?: number } }
  historico: { em: string; descricao: string; ator: string | null }[]
}

const ESTADOS_OCORRENCIA = [
  { estado: 'EmAvaliacao', rotulo: 'Em avaliação' },
  { estado: 'EquipeDesignadaFicticia', rotulo: 'Equipe designada (fictícia)' },
  { estado: 'ConcluidoFicticio', rotulo: 'Concluído (fictício)' },
]
const ESTADOS_OFERTA = [
  { estado: 'Aceita', rotulo: 'Aceitar participação' },
  { estado: 'Recusada', rotulo: 'Recusar' },
  { estado: 'Concluida', rotulo: 'Marcar como concluída' },
]

/** Tela da central receptora SIMULADA: mostra o que a central efetivamente recebeu e persistiu. */
export function CentralSimulada() {
  const { codigo = '' } = useParams()
  const { usuario } = useSessao()
  const { dados, erro, recarregar } = useDados<{ aviso: string; recebimentos: Recebimento[] }>(usuario ? `/simulador/centrais/${codigo}/recebimentos` : null, { intervaloMs: 10000 })
  useEvento('ocorrenciaAtualizada', () => { void recarregar() })
  useEvento('ofertaAtualizada', () => { void recarregar() })
  const [msg, setMsg] = useState<string | null>(null)

  const atualizar = async (protocolo: string, estado: string) => {
    try {
      const r = await api<{ retornoEntregue: boolean }>(`/simulador/centrais/${codigo}/recebimentos/${protocolo}/estado`, { method: 'PATCH', json: { estado, nota: null } })
      setMsg(r.retornoEntregue ? 'Estado atualizado e retorno entregue ao Alerta em Ação por HTTP.' : 'Estado atualizado na central; retorno ao Alerta em Ação falhou.')
      void recarregar()
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }

  if (!usuario) return <Cartao><p>Entre com o perfil do operador em <Link to="/perfil" className="text-oceano underline">Perfil</Link>.</p></Cartao>

  return (
    <div className="grid gap-4">
      <div>
        <h1 className="text-[26px] font-bold text-petroleo">Central receptora {codigo}</h1>
        <p className="text-[14px] text-tinta-suave">Receptor HTTP com autenticação, validação de esquema, persistência e protocolo próprio.</p>
      </div>
      <AvisoSimulado texto="CENTRAL SIMULADA — AMBIENTE DE DEMONSTRAÇÃO. NENHUM ÓRGÃO REAL FOI ACIONADO." />
      {erro && <p role="alert" className="text-emergencia-escura">{erro}</p>}
      {msg && <p role="status" className="text-[14px] text-seguro">{msg}</p>}
      {(dados?.recebimentos ?? []).length === 0 ? <Vazio>Esta central ainda não recebeu mensagens.</Vazio> : (
        <ul className="grid gap-3 md:grid-cols-2">
          {dados!.recebimentos.map((r) => {
            const m = r.mensagem
            const oferta = r.tipoMensagem === 'oferta_apoio'
            return (
              <li key={r.protocolo}>
                <Cartao titulo={r.protocolo} acao={<Selo tom={r.estado === 'Recebido' ? 'espera' : 'ok'}>{r.estado}</Selo>}>
                  <p className="text-[13px] text-tinta-suave">
                    {oferta ? 'Oferta de apoio' : r.tipoMensagem === 'atualizacao_ocorrencia' ? 'Atualização de ocorrência' : 'Ocorrência'}, {nomeMunicipio(r.municipioCodigo)}, recebida {formatarHora(r.recebidoEm)}{r.duplicatas ? `, ${r.duplicatas} reenvio(s) reconhecido(s) sem duplicar` : ''}
                  </p>
                  {oferta ? (
                    <div className="mt-2 text-[14px]"><p className="font-semibold">{m.descricao as string}</p><p>Tipo: {m.tipoAjuda as string}</p>{m.contatoAutorizado && <p>Contato autorizado: {m.contatoAutorizado as string}</p>}</div>
                  ) : (
                    <div className="mt-2 text-[14px]">
                      <p><span className="font-medium">Riscos:</span> {(m.tiposRisco ?? []).join(', ')}</p>
                      {m.pessoasEnvolvidas?.quantidade != null && <p><span className="font-medium">Pessoas:</span> {m.pessoasEnvolvidas.quantidade}{m.pessoasEnvolvidas.feridos ? `, ${m.pessoasEnvolvidas.feridos} ferido(s)` : ''}</p>}
                      {m.latitude != null && <p className="numero-tabular"><span className="font-medium">Local:</span> {Number(m.latitude).toFixed(5)}, {Number(m.longitude).toFixed(5)}</p>}
                      {m.relato && <p className="mt-1">“{m.relato}”</p>}
                      {(m.sinalizacoes ?? []).length > 0 && <p className="mt-1 text-[13px]">Sinalizações: {m.sinalizacoes!.map((s) => s.titulo).join('; ')}</p>}
                    </div>
                  )}
                  <div className="mt-3 flex flex-wrap gap-2">
                    {(oferta ? ESTADOS_OFERTA : ESTADOS_OCORRENCIA).map((e) => (
                      <Botao key={e.estado} variante="secundario" disabled={r.estado === e.estado} onClick={() => atualizar(r.protocolo, e.estado)}>{e.rotulo}</Botao>
                    ))}
                  </div>
                  <details className="mt-2">
                    <summary className="cursor-pointer text-[13px] text-oceano">Histórico na central</summary>
                    <ol className="mt-1 text-[12.5px]">{r.historico.map((h, i) => <li key={i}>{formatarHora(h.em)}: {h.descricao}</li>)}</ol>
                  </details>
                </Cartao>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

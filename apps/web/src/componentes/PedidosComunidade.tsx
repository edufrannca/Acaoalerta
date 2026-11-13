import { useState } from 'react'
import { Botao, Selo, classeEntrada } from './Base'
import { IcAjudar, IcRelogio, IcLocal, IcPessoa } from './Icones'
import { formatarHora } from '../lib/api'
import { uuid } from '../lib/dispositivo'
import { enfileirar } from '../lib/filaOffline'
import { TIPOS_AJUDA } from '../lib/municipios'
import type { PedidoComunitario, RespostaComunidade } from '../lib/comunidade'

/** Tipo de ajuda sugerido a partir da necessidade declarada. */
const SUGESTAO: Record<string, string> = {
  agua: 'DoacaoAgua', alimentos: 'DoacaoAlimentos', abrigo: 'ApoioEmAbrigo', transporte_assistido: 'TransporteAssistido',
  itens_higiene: 'KitHigiene', roupas: 'Roupas', outros: 'ApoioLogisticoSeguro',
}

export function CartaoPedido({ p, destaque, aoOferecer, compacto }: { p: PedidoComunitario; destaque?: boolean; aoOferecer?: (p: PedidoComunitario) => void; compacto?: boolean }) {
  const grupos = [
    p.criancas && `Crianças: ${p.criancas}`,
    p.idosos && `Idosos: ${p.idosos}`,
    p.mobilidadeReduzida && `Mobilidade reduzida: ${p.mobilidadeReduzida}`,
  ].filter(Boolean) as string[]
  return (
    <article id={`pedido-${p.id}`} aria-label={`Pedido ${p.referencia}`}
      className={`rounded-cartao bg-branco p-3.5 ring-1 ${destaque ? 'ring-2 ring-emergencia' : 'ring-linha'}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="text-[17px] font-bold leading-snug text-petroleo">{p.tipos.join(' · ')}</h3>
        {p.demonstrativo && <Selo tom="demo">Fictício</Selo>}
      </div>
      <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[13.5px] text-tinta-suave">
        <span className="inline-flex items-center gap-1"><IcLocal tamanho={15} />{p.municipio ?? 'Município não identificado'}{p.distanciaKm == null ? '' : p.distanciaKm === 0 ? ', perto de você' : `, a cerca de ${String(p.distanciaKm).replace('.', ',')} km`}</span>
        <span className="inline-flex items-center gap-1"><IcRelogio tamanho={15} />por volta de {formatarHora(p.horarioAproximado)}</span>
        <span>Ref. {p.referencia}</span>
      </p>
      <div className="mt-2 flex flex-wrap gap-1.5">
        <Selo tom={p.situacao.startsWith('Confirmado') || p.situacao.startsWith('Em atendimento') ? 'ok' : 'espera'}>{p.situacao}</Selo>
        <Selo tom={p.verificacao === 'Verificado pela equipe' ? 'ok' : 'neutro'}>{p.verificacao}</Selo>
      </div>
      {!compacto && (
        <dl className="mt-2 grid gap-1 text-[14.5px]">
          {p.pessoas != null && <div className="flex gap-1.5"><IcPessoa tamanho={18} /><dt className="sr-only">Pessoas</dt><dd>{p.pessoas} {p.pessoas === 1 ? 'pessoa' : 'pessoas'} no local</dd></div>}
          {grupos.length > 0 && <div><dt className="sr-only">Grupos que precisam de atenção</dt><dd>{grupos.join(' · ')}</dd></div>}
          {p.pessoas == null && grupos.length === 0 && <div><dd className="text-tinta-suave">Quantidade de pessoas não compartilhada.</dd></div>}
          {p.necessidades && p.necessidades.length > 0 && (
            <div><dt className="font-semibold">Precisa de</dt><dd className="mt-1 flex flex-wrap gap-1.5">{p.necessidades.map((n) => <span key={n.codigo} className="rounded-full bg-seguro-claro px-2.5 py-0.5 text-[13.5px] font-medium text-seguro">{n.rotulo}</span>)}</dd></div>
          )}
        </dl>
      )}
      {!compacto && (!p.necessidades || p.necessidades.length === 0) && <p className="mt-2 text-[13px] text-tinta-suave">Necessidades não informadas ou não compartilhadas. Você ainda pode oferecer apoio, sujeito à avaliação da equipe.</p>}
      {p.ofertasEmAndamento > 0 && <p className="mt-2 text-[13.5px] text-tinta-suave">{p.ofertasEmAndamento} oferta(s) de ajuda em avaliação ou andamento.</p>}
      {aoOferecer && <Botao className="mt-3 w-full" variante="secundario" onClick={() => aoOferecer(p)}><IcAjudar tamanho={18} /> Oferecer ajuda</Botao>}
    </article>
  )
}

export function ResumoRegiao({ r }: { r: RespostaComunidade }) {
  const a = r.agregados
  return (
    <div className="rounded-cartao bg-fundo p-3 text-[14px]">
      {a ? (
        <>
          <p className="font-semibold text-tinta">{a.pedidos} pedidos compartilhados na busca · {a.pessoasInformadas} pessoas informadas</p>
          <p className="text-tinta-suave">Com crianças: {a.pedidosComCriancas} · com idosos: {a.pedidosComIdosos} · com mobilidade reduzida: {a.pedidosComMobilidadeReduzida}</p>
          {a.necessidades.length > 0 && <p className="text-tinta-suave">Necessidades mais citadas: {a.necessidades.slice(0, 4).map((n) => `${n.rotulo} (${n.pedidos})`).join(', ')}</p>}
        </>
      ) : <p className="text-tinta-suave">{r.agregadosOmitidos ?? 'Nenhum pedido compartilhado nesta busca.'}</p>}
      {r.naoCompartilhados > 0 && <p className="mt-1 text-tinta-suave">{r.naoCompartilhados} pedido(s) recente(s) não foram compartilhados pelos solicitantes e não aparecem aqui.</p>}
    </div>
  )
}

/** Oferta de ajuda a um pedido: só depois de ler e aceitar as orientações de segurança. */
export function FormOferta({ p, aoFechar, aoEnviar }: { p: PedidoComunitario; aoFechar: () => void; aoEnviar: (msg: string) => void }) {
  const [tipo, setTipo] = useState(SUGESTAO[p.necessidades?.[0]?.codigo ?? ''] ?? 'ApoioLogisticoSeguro')
  const [descricao, setDescricao] = useState('')
  const [disponibilidade, setDisponibilidade] = useState('')
  const [contato, setContato] = useState('')
  const [autorizo, setAutorizo] = useState(false)
  const [ciente, setCiente] = useState(false)
  const enviar = async () => {
    const id = uuid()
    await enfileirar({ id, tipo: 'oferta_pedido', caminho: `/api/comunidade/pedidos/${p.id}/ofertas`, corpo: {
      id, tipo, descricao, disponibilidade: disponibilidade || null, contatoAutorizado: autorizo ? contato || null : null, cienteSeguranca: ciente,
    } })
    aoEnviar(`Oferta para o pedido ${p.referencia} registrada. A equipe responsável avalia e, se aceitar, envia orientações em Minhas ofertas.`)
    aoFechar()
  }
  return (
    <section aria-labelledby="titulo-oferta" className="rounded-cartao bg-branco p-4 ring-2 ring-oceano">
      <h3 id="titulo-oferta" className="text-[18px] font-bold text-petroleo">Oferecer ajuda ao pedido {p.referencia}</h3>
      <div role="note" className="mt-2 rounded-controle bg-atencao-claro p-3 text-[14px] text-tinta">
        <p className="font-semibold">Ajuda comunitária não é resgate.</p>
        <p>Não entre em água, correnteza, encostas ou imóveis danificados. Você não recebe o endereço da família: a equipe avalia sua oferta e indica um ponto seguro de entrega.</p>
      </div>
      <div className="mt-3 grid gap-3">
        <label className="block"><span className="text-[14px] font-medium">Tipo de ajuda</span>
          <select className={classeEntrada} value={tipo} onChange={(e) => setTipo(e.target.value)}>
            {Object.entries(TIPOS_AJUDA).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label className="block"><span className="text-[14px] font-medium">O que você oferece</span>
          <input className={classeEntrada} value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={500} placeholder="Ex.: 12 garrafas de água de 1,5 L" />
        </label>
        <label className="block"><span className="text-[14px] font-medium">Quando pode entregar</span>
          <input className={classeEntrada} value={disponibilidade} onChange={(e) => setDisponibilidade(e.target.value)} placeholder="Ex.: hoje até 18h" />
        </label>
        <label className="block"><span className="text-[14px] font-medium">Contato para a equipe (opcional)</span>
          <input className={classeEntrada} value={contato} onChange={(e) => setContato(e.target.value)} placeholder="Telefone ou e-mail" />
        </label>
        <label className="flex items-start gap-2 text-[14px]"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={autorizo} onChange={(e) => setAutorizo(e.target.checked)} />Autorizo a equipe responsável a ver meu contato</label>
        <label className="flex items-start gap-2 text-[14px] font-medium"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={ciente} onChange={(e) => setCiente(e.target.checked)} />Li as orientações e não vou entrar em áreas perigosas</label>
        <div className="flex gap-2">
          <Botao variante="primario" className="flex-1" disabled={!ciente || descricao.trim().length < 3} onClick={enviar}>Enviar oferta</Botao>
          <Botao variante="secundario" onClick={aoFechar}>Cancelar</Botao>
        </div>
      </div>
    </section>
  )
}

import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Botao, Cartao, classeEntrada } from '../componentes/Base'
import { type ItemFila, uuid } from '../lib/dispositivo'
import { descricaoEstado, enfileirar, observarFila } from '../lib/filaOffline'
import { formatarHora } from '../lib/api'
import { useLocalizacao } from '../lib/hooks'

export function EstouSeguro() {
  const [params] = useSearchParams()
  const avisoId = params.get('aviso') || null
  const { posicao, iniciar, estado } = useLocalizacao()
  const [compartilhar, setCompartilhar] = useState(true)
  const [ajudar, setAjudar] = useState(false)
  const [mensagem, setMensagem] = useState('')
  const [id, setId] = useState<string | null>(null)
  const [item, setItem] = useState<ItemFila | null>(null)

  useEffect(() => { iniciar() }, [iniciar])
  useEffect(() => (id ? observarFila((l) => setItem(l.find((x) => x.id === id) ?? null)) : undefined), [id])

  const confirmar = async () => {
    const novo = uuid()
    await enfileirar({
      id: novo, tipo: 'situacao', caminho: '/api/situacoes',
      corpo: { id: novo, status: 'Seguro', declaradaEm: new Date().toISOString(), latitude: compartilhar ? posicao?.latitude : null, longitude: compartilhar ? posicao?.longitude : null, avisoId, desejaAjudar: ajudar, mensagem: mensagem || null },
    })
    setId(novo)
  }

  if (id) {
    const recebido = item?.estado === 'recebido_servidor'
    return (
      <div className="mx-auto grid max-w-xl gap-4">
        <section aria-live="polite" className={`rounded-cartao p-5 ${recebido ? 'bg-seguro text-branco' : 'bg-[#FBF1DC]'}`}>
          <h1 className="text-[24px] font-bold">{recebido ? 'Situação registrada' : 'Situação salva neste aparelho'}</h1>
          <p className="mt-1">{item ? descricaoEstado[item.estado] : ''}{recebido && item?.recebidoEm ? ` Horário: ${formatarHora(item.recebidoEm)}.` : ''}</p>
          <p className="mt-2 text-[14px] opacity-90">"Estou seguro" é uma autodeclaração. Não certifica a segurança da sua casa ou da região.</p>
        </section>
        {ajudar && <Link to="/ajudar" className="rounded-cartao bg-oceano p-4 text-center text-[17px] font-semibold text-branco">Ver como ajudar agora</Link>}
        <Botao variante="secundario" onClick={() => { setId(null); setItem(null) }}>Atualizar minha situação</Botao>
      </div>
    )
  }

  return (
    <div className="mx-auto grid max-w-xl gap-4">
      <h1 className="text-[26px] font-bold text-petroleo">Avise que você está bem</h1>
      <Cartao>
        <label className="flex items-start gap-3 text-[15px]">
          <input type="checkbox" className="mt-1 size-5 accent-oceano" checked={compartilhar} onChange={(e) => setCompartilhar(e.target.checked)} />
          <span>Incluir minha localização atual{posicao ? ` (±${posicao.precisao ?? '?'} m)` : estado === 'negado' ? ' (permissão negada)' : ' (obtendo…)'}</span>
        </label>
        <label className="mt-3 flex items-start gap-3 text-[15px]">
          <input type="checkbox" className="mt-1 size-5 accent-oceano" checked={ajudar} onChange={(e) => setAjudar(e.target.checked)} />
          <span>Estou seguro e posso ajudar a comunidade</span>
        </label>
        <label className="mt-3 block">
          <span className="text-[14px] font-medium">Recado (opcional)</span>
          <input className={classeEntrada} maxLength={300} value={mensagem} onChange={(e) => setMensagem(e.target.value)} placeholder="Ex.: estamos na casa da minha irmã" />
        </label>
        <Botao className="mt-4 w-full" variante="seguro" onClick={confirmar}>Confirmar que estou seguro</Botao>
        {avisoId && <p className="mt-2 text-[13px] text-tinta-suave">Resposta vinculada ao aviso recebido.</p>}
      </Cartao>
    </div>
  )
}

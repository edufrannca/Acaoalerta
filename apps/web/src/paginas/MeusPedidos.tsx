import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { Botao, Cartao, Selo, Vazio } from '../componentes/Base'
import { formatarHora } from '../lib/api'
import type { ItemFila } from '../lib/dispositivo'
import { descricaoEstado, observarFila, sincronizar } from '../lib/filaOffline'
import { useOnline } from '../lib/hooks'

const TIPOS: Record<ItemFila['tipo'], string> = { socorro: 'Pedido de socorro', complemento: 'Informações do pedido', situacao: 'Estou seguro', localizacao: 'Localização', oferta: 'Oferta de ajuda' }
const TOM = { salvo_no_aparelho: 'atencao', transmitindo: 'espera', recebido_servidor: 'ok', recusado: 'erro' } as const

export function MeusPedidos() {
  const [itens, setItens] = useState<ItemFila[]>([])
  const online = useOnline()
  const [sincronizando, setSincronizando] = useState(false)
  useEffect(() => observarFila(setItens), [])
  const pendentes = itens.filter((i) => i.estado === 'salvo_no_aparelho').length

  return (
    <div className="mx-auto grid max-w-2xl gap-4">
      <h1 className="text-[26px] font-bold text-petroleo">Meus pedidos e envios</h1>
      <Cartao>
        <p className="text-[14.5px]">Tudo que você envia fica primeiro neste aparelho. Mostramos aqui se cada envio chegou ao servidor, sem confundir "salvo" com "transmitido".</p>
        <div className="mt-3 flex flex-wrap items-center gap-3">
          <Selo tom={pendentes ? 'atencao' : 'ok'}>{pendentes ? `${pendentes} aguardando envio` : 'Nada pendente'}</Selo>
          <Botao variante="secundario" disabled={!online || sincronizando || !pendentes} onClick={async () => { setSincronizando(true); await sincronizar(); setSincronizando(false) }}>
            {sincronizando ? 'Enviando…' : 'Enviar pendentes agora'}
          </Botao>
        </div>
      </Cartao>
      {itens.length === 0 ? <Vazio>Nenhum envio feito neste aparelho.</Vazio> : (
        <ul className="grid gap-2">
          {itens.map((i) => (
            <li key={i.id} className="rounded-cartao bg-branco p-4 ring-1 ring-linha/70" data-testid="item-fila" data-estado={i.estado}>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-semibold text-petroleo">{TIPOS[i.tipo]}</span>
                <Selo tom={TOM[i.estado]}>{descricaoEstado[i.estado]}</Selo>
              </div>
              <p className="numero-tabular mt-1 text-[13px] text-tinta-suave">
                Criado {formatarHora(i.criadoEm)}{i.recebidoEm ? `, recebido ${formatarHora(i.recebidoEm)}` : ''}{i.tentativas ? `, ${i.tentativas} tentativa(s)` : ''}
              </p>
              {i.ultimoErro && i.estado !== 'recebido_servidor' && <p className="text-[13px] text-atencao">{i.ultimoErro}</p>}
              {i.tipo === 'socorro' && <Link className="mt-2 inline-block text-[14px] font-medium text-oceano underline-offset-2 hover:underline" to={`/socorro/${i.id}`}>Acompanhar pedido</Link>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AvisoSimulado, Botao, Cartao, Selo } from '../componentes/Base'
import { ContatosEmergencia } from '../componentes/Contatos'
import { formatarHora } from '../lib/api'
import type { ItemFila } from '../lib/dispositivo'
import { enfileirar, observarFila, transmitir } from '../lib/filaOffline'
import { NECESSIDADES, decodificarQr } from '../lib/comunidade'
import { useLocalizacao, useOnline } from '../lib/hooks'

const SITUACOES: Record<string, string> = {
  ilhado: 'Pessoas ilhadas', aguaSubindo: 'Água subindo', correnteza: 'Correnteza', riscoDeslizamento: 'Risco de deslizamento',
  desmoronamento: 'Desabamento', barreiraComSinaisInstabilidade: 'Barreira com rachaduras', acessoBloqueado: 'Acesso bloqueado',
  equipamentoMedicoEssencial: 'Uso de equipamento médico', mobilidadeReduzida: 'Alguém não consegue andar', sozinho: 'Pessoa sozinha',
  riscoSegurancaPublica: 'Risco à segurança',
}

interface Resposta { referencia: string; duplicado: boolean; recebidoEm: string; nivelComprovado: number; nivelDescricao: string; localizacaoEmissor: string; observacao: string }

/**
 * Aparelho que AJUDA: abriu o link do QR mostrado por uma família sem internet.
 * Guarda o pedido no próprio aparelho primeiro (sobrevive a ficar sem sinal) e encaminha ao servidor.
 */
export function Retransmitir() {
  const pedido = useMemo(() => decodificarQr(location.hash), [])
  const online = useOnline()
  const [incluirPosicao, setIncluirPosicao] = useState(false)
  const { posicao, estado: gps, iniciar } = useLocalizacao(10000)
  const [item, setItem] = useState<ItemFila | null>(null)
  const idFila = pedido ? `retransmissao-${pedido.id}` : ''

  useEffect(() => (idFila ? observarFila((l) => setItem(l.find((x) => x.id === idFila) ?? null)) : undefined), [idFila])
  useEffect(() => { if (incluirPosicao) iniciar() }, [incluirPosicao, iniciar])

  if (!pedido) {
    return (
      <Cartao titulo="Código não reconhecido">
        <p className="text-[15px]">Este link não contém um pedido válido do Alerta em Ação. Peça para a pessoa mostrar o código de novo e aponte a câmera para ele.</p>
        <p className="mt-2 text-[15px]">Em risco imediato, ligue <a className="font-bold underline" href="tel:193">193</a> (Bombeiros) ou <a className="font-bold underline" href="tel:199">199</a> (Defesa Civil).</p>
      </Cartao>
    )
  }

  const encaminhar = async () => {
    await enfileirar({ id: idFila, tipo: 'retransmissao', caminho: '/api/retransmissoes/qr', corpo: {
      pedido,
      latitudeRetransmissor: incluirPosicao && posicao ? Number(posicao.latitude.toFixed(5)) : null,
      longitudeRetransmissor: incluirPosicao && posicao ? Number(posicao.longitude.toFixed(5)) : null,
      precisaoRetransmissor: incluirPosicao && posicao ? posicao.precisao : null,
    } })
  }

  const resp = item?.resposta as Resposta | undefined
  const marcas = Object.entries(pedido.m).filter(([, v]) => v).map(([k]) => SITUACOES[k] ?? k)
  const nec = pedido.nec.map((n) => NECESSIDADES.find((x) => x.codigo === n)?.rotulo ?? n)
  const estado = !item ? null
    : item.estado === 'recebido_servidor' ? (resp?.nivelComprovado === 5 ? 'A equipe de teste recebeu o pedido' : 'O pedido de ajuda foi registrado!')
    : item.estado === 'transmitindo' ? 'Estamos enviando o pedido de ajuda.'
    : item.estado === 'recusado' ? 'Não foi possível registrar o pedido' : 'O pedido está salvo neste celular. Ainda não foi enviado.'

  return (
    <div className="mx-auto grid max-w-2xl gap-4">
      <section className="rounded-cartao bg-emergencia p-5 text-branco">
        <h1 className="text-[24px] font-bold leading-tight">Alguém por perto pediu socorro</h1>
        <p className="mt-1 text-[15.5px]">O aparelho dessa pessoa está sem internet. Você pode encaminhar o pedido pela sua conexão. Se houver risco imediato, ligue também.</p>
        <p className="mt-2 text-[13.5px] opacity-90">Ref. {pedido.id.replace(/-/g, '').slice(0, 6).toUpperCase()} · criado às {formatarHora(pedido.criado)}</p>
      </section>

      <Cartao titulo="O que o pedido informa">
        <ul className="grid gap-1 text-[15px]">
          <li>{marcas.length ? marcas.join(' · ') : 'Situação não detalhada'}</li>
          {pedido.pessoas != null && <li>{pedido.pessoas} pessoa(s) no local</li>}
          {nec.length > 0 && <li>Precisa de: {nec.join(', ')}</li>}
          <li>{pedido.lat != null ? `Localização registrada pelo aparelho da família (±${pedido.prec ?? '?'} m).` : 'Localização não informada pelo aparelho da família.'}</li>
          {pedido.d && <li><Selo tom="demo">Pedido fictício (demonstração)</Selo></li>}
        </ul>
        <p className="mt-2 text-[13px] text-tinta-suave">Sem nome, telefone ou relato. Você não precisa ficar com estes dados: depois de encaminhado, nada além do estado fica neste aparelho.</p>
      </Cartao>

      {!item ? (
        <Cartao titulo="Encaminhar pelo meu aparelho">
          <label className="flex items-start gap-2 text-[14.5px]">
            <input type="checkbox" className="mt-1 size-5 accent-oceano" checked={incluirPosicao} onChange={(e) => setIncluirPosicao(e.target.checked)} />
            <span>Enviar também a posição aproximada deste aparelho. Ela fica separada e nunca substitui a posição da família.</span>
          </label>
          {incluirPosicao && <p className="mt-1 pl-7 text-[13px] text-tinta-suave">{posicao ? `Posição deste aparelho: ±${posicao.precisao ?? '?'} m.` : gps === 'negado' ? 'Localização negada; o pedido segue sem ela.' : 'Obtendo localização…'}</p>}
          <Botao className="mt-4 w-full" variante="perigo" onClick={encaminhar}>Encaminhar pedido</Botao>
          {!online && <p className="mt-2 text-[14px]">Este aparelho também está sem internet. O pedido fica guardado aqui e é enviado assim que a conexão voltar.</p>}
        </Cartao>
      ) : (
        <section aria-live="polite" className={`rounded-cartao p-5 ${item.estado === 'recebido_servidor' ? 'bg-petroleo text-branco' : 'bg-[#FBF1DC] text-tinta'}`}>
          <h2 className="text-[21px] font-bold">{estado}</h2>
          {item.estado === 'recebido_servidor' && resp ? (
            <>
              <p className="mt-1 text-[15px]">Nível {resp.nivelComprovado}: {resp.nivelDescricao}. Ref. {resp.referencia}, {formatarHora(resp.recebidoEm)}.{resp.duplicado ? ' Este pedido já estava registrado; nada foi duplicado.' : ''}</p>
              <p className="mt-2 text-[14px] opacity-90">Mostre esta tela à pessoa que pediu ajuda. {resp.observacao}</p>
            </>
          ) : item.estado === 'recusado' ? (
            <p className="mt-1">{item.ultimoErro}</p>
          ) : (
            <>
              <p className="mt-1 text-[15px]">Guardado neste aparelho às {formatarHora(item.criadoEm)}. {online ? 'Enviando…' : 'Sem internet: envio automático quando a conexão voltar.'}</p>
              {item.ultimoErro && <p className="mt-1 text-[13.5px]">Última tentativa: {item.ultimoErro} ({item.tentativas}x)</p>}
              <Botao className="mt-3" variante="primario" onClick={() => transmitir(item.id)}>Tentar enviar agora</Botao>
            </>
          )}
        </section>
      )}

      <Cartao titulo="Ligue também">
        <ContatosEmergencia compacto destaque={['193', '199', '192', '190']} />
      </Cartao>
      <AvisoSimulado texto="Centrais SIMULADAS de demonstração: nenhum órgão real é acionado pelo Alerta em Ação." />
      <Link to="/" className="text-center text-[15px] font-semibold text-oceano underline">Ir para o início</Link>
    </div>
  )
}

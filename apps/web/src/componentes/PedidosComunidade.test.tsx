import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { CartaoPedido } from './PedidosComunidade'
import type { PedidoComunitario } from '../lib/comunidade'

const pedido: PedidoComunitario = {
  id: 'pedido-um',
  referencia: 'ABC123',
  tipos: ['Alagamento ou pessoas ilhadas'],
  pessoas: null,
  criancas: null,
  idosos: null,
  mobilidadeReduzida: null,
  necessidades: null,
  horarioAproximado: '2026-11-13T08:00:00Z',
  situacao: 'Recebido pelo servidor, aguardando confirmação',
  verificacao: 'Não verificado',
  municipio: 'Recife',
  area: { latitude: -8.05, longitude: -34.9, raioMetros: 400 },
  distanciaKm: 0.2,
  ofertasEmAndamento: 0,
  // O cliente não deve bloquear oferta apenas porque faltam necessidades declaradas.
  podeOferecerAjuda: false,
  demonstrativo: true,
}

describe('Pedido compartilhado e ofertas comunitárias', () => {
  afterEach(() => cleanup())

  it('mostra Oferecer ajuda até para um pedido sem necessidades declaradas', () => {
    const aoOferecer = vi.fn()
    render(<CartaoPedido p={pedido} aoOferecer={aoOferecer} />)
    expect(screen.getByText(/Necessidades não informadas ou não compartilhadas/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Oferecer ajuda/i }))
    expect(aoOferecer).toHaveBeenCalledWith(pedido)
  })

  it('não exibe uma ação de oferta num cartão sem callback de autorização', () => {
    render(<CartaoPedido p={pedido} />)
    expect(screen.queryByRole('button', { name: /Oferecer ajuda/i })).toBeNull()
  })
})

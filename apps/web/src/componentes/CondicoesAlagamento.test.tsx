import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import type { IndicadorAlagamento } from './CondicoesAlagamento'

let atual: IndicadorAlagamento | null = null
vi.mock('../lib/hooks', () => ({ useDados: () => ({ dados: atual, doCache: null, erro: null, carregando: false, recarregar: async () => {} }) }))
vi.mock('../lib/tempoReal', () => ({ useEvento: () => {} }))
const { CondicoesAlagamento, textoFala } = await import('./CondicoesAlagamento')

const SEM = 'Há condições que exigem atenção, mas não existem dados suficientes para estimar quando poderá ocorrer um alagamento.'
const base = (nivel: IndicadorAlagamento['nivel'], rotulo: string): IndicadorAlagamento => ({
  regiao: 'Casa, Olinda', nivel, rotulo, explicacao: 'A estação próxima registrou chuva forte na última hora.', explicacaoOrigem: 'regras', explicacaoObservacao: null,
  justificativa: ['Chuva forte medida a 400 m.'],
  estacao: { nome: 'Olinda (sintética)', fonte: 'APAC/SIRH', distanciaMetros: 400, leituraEm: '2026-11-13T12:00:00Z', atual: true, mm1h: 18, mm3h: 20, mm6h: null, mm12h: null, mm24h: 40, mm48h: null, mm72h: null },
  intensidade: 'Chuva forte (10 a 50 mm/h)', tendencia: 'Tendência crescente', avisos: [], ocorrenciasVerificadas: 0, relatosNaoVerificados: 0, suscetibilidadeDocumentada: null, estacoesDivergentes: false,
  estimativaTemporal: { disponivel: false, mensagem: SEM },
  separacao: { chuvaObservada: 'Medida pela estação.', previsaoOficial: 'Nenhum aviso meteorológico aplicável.', estimativaExperimental: SEM, alagamentoConfirmado: 'Nenhum alagamento confirmado por operador nesta região.' },
  confiabilidade: { nivel: 'alta', motivos: ['Estação a menos de 2 km.'] }, limitacoes: ['Sem dados de maré.'], orientacoes: ['Evite atravessar áreas alagadas.'],
  avaliadoEm: '2026-11-13T12:05:00Z', validoAte: '2026-11-13T14:00:00Z', criterios: 'alagamento-2026.11.1', fontes: [],
})

describe('Condições de chuva e atenção a alagamentos', () => {
  afterEach(() => cleanup())

  it('mostra nível por texto (não só cor), estação, distância, fonte, horário e frase sem estimativa', () => {
    atual = base('Elevado', 'Elevado')
    render(<CondicoesAlagamento latitude={-8} longitude={-34.85} />)
    expect(screen.getByRole('region', { name: /Condições de chuva e atenção a alagamentos/ })).toBeTruthy()
    expect(screen.getByText(/Nível de atenção a alagamentos:/)).toBeTruthy()
    expect(screen.getByText('(2 de 3)')).toBeTruthy()
    expect(screen.getByText(/Olinda \(sintética\), a 400 m/)).toBeTruthy()
    expect(screen.getAllByText(SEM).length).toBeGreaterThan(0)
    expect(screen.getByText(/Fontes: APAC e INMET/)).toBeTruthy()
    expect(screen.getByRole('button', { name: /Ouvir informações/ })).toBeTruthy()
    expect(document.body.textContent).not.toMatch(/\d+ ?%/)
  })

  it('fala contém o mesmo conteúdo do texto (nada só sonoro) e leitura antiga é avisada por escrito', () => {
    const i = base('Atencao', 'Atenção'); i.estacao!.atual = false; atual = i
    render(<CondicoesAlagamento latitude={-8} longitude={-34.85} />)
    expect(screen.getByText(/Leitura antiga: não é tempo real/)).toBeTruthy()
    const f = textoFala(i)
    expect(f).toContain('Atenção'); expect(f).toContain(SEM); expect(f).toContain('Leitura antiga')
  })

  it('sem posição orienta sem consultar e sem evidência não mostra ícone de alerta', () => {
    atual = null
    render(<CondicoesAlagamento latitude={null} longitude={null} />)
    expect(screen.getByText(/Ative a localização/)).toBeTruthy()
    cleanup(); atual = base('SemEvidenciaSuficiente', 'Sem evidência suficiente')
    render(<CondicoesAlagamento latitude={-8} longitude={-34.85} />)
    expect(screen.getByText('(0 de 3)')).toBeTruthy()
  })
})

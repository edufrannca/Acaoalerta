export type TipoLocal = 'Residencia' | 'Trabalho' | 'Outro'
export type OrigemPosicao = 'GpsAtual' | 'CepGeocodificado' | 'EnderecoGeocodificado' | 'EscolhidoNoMapa'

export interface Endereco { cep: string | null; logradouro: string | null; numero: string | null; complemento: string | null; bairro: string | null; municipio: string | null; uf: string | null }
export interface Moradores { total: number | null; criancas: number | null; idosos: number | null; mobilidadeReduzida: number | null; dependemAssistencia: number | null; contatoReferencia: string | null; observacoes: string | null }

export interface LocalMonitorado {
  id: string; apelido: string; tipo: TipoLocal; moroAqui: boolean; latitude: number; longitude: number; municipioCodigo: string | null; municipio: string | null
  origem: OrigemPosicao; precisao: string | null; endereco: Endereco; moradores: Moradores; criadoEm: string; atualizadoEm: string
}

export interface ResultadoGeo { latitude: number; longitude: number; descricao: string; precisao: string; exigeConfirmacaoNoMapa: boolean }

export interface Permissoes { receberNotificacoes: boolean; verNecessidades: boolean; verMoradores: boolean; verPosicaoDoPedido: boolean }

export const ROTULO_TIPO: Record<TipoLocal, string> = { Residencia: 'Residência', Trabalho: 'Trabalho', Outro: 'Outro' }
export const ROTULO_ORIGEM: Record<OrigemPosicao, string> = {
  GpsAtual: 'GPS do aparelho', CepGeocodificado: 'CEP + OpenStreetMap', EnderecoGeocodificado: 'Endereço + OpenStreetMap', EscolhidoNoMapa: 'Escolhido no mapa',
}

/** Mesmos textos do servidor (versão 2026-11-13.1). Todos opcionais e desmarcados. */
export const CONSENTIMENTOS = [
  { chave: 'alertasPreventivos', finalidade: 'AlertasPreventivos', titulo: 'Alertas preventivos', texto: 'Quero receber alertas e informações relacionadas aos locais que escolhi monitorar.' },
  { chave: 'compartilhamentoOperacional', finalidade: 'CompartilhamentoOperacional', titulo: 'Compartilhamento operacional', texto: 'Autorizo o compartilhamento dos dados necessários ao atendimento e das atualizações de localização durante um pedido de socorro ativo com equipes autorizadas. Nesta versão do projeto, as equipes e centrais são simuladas.' },
  { chave: 'compartilhamentoComunitario', finalidade: 'CompartilhamentoComunitario', titulo: 'Compartilhamento comunitário', texto: 'Autorizo que novos pedidos de ajuda mostrem um resumo das necessidades e da região aproximada para que outras pessoas possam oferecer apoio.' },
  { chave: 'notificacaoFamiliares', finalidade: 'NotificacaoFamiliares', titulo: 'Notificação de familiares vinculados', texto: 'Autorizo que familiares vinculados e aprovados recebam notificações e informações permitidas sobre pedidos de socorro associados à minha residência.' },
] as const

export const NOTA_SOCORRO = 'Pedir socorro nunca depende destas autorizações. Você pode mudar ou revogar cada uma em Perfil.'

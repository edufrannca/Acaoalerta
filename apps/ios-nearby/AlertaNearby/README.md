# PoC Nearby — iOS (esqueleto, não compilado)

Arquivo principal: `NearbyPoC.swift`. Usa o SDK multiplataforma
[google/nearby](https://github.com/google/nearby) (Swift Package `NearbyConnections`),
que permite interoperar com o Android (`play-services-nearby`) usando o mesmo `serviceId`
(`br.alertaemacao.nearby.SOCORRO`) e estratégia de cluster.

**Estado honesto:** código escrito sem Xcode/dispositivos disponíveis no ambiente de desenvolvimento.
Não foi compilado nem testado. Requer Xcode 16+, iOS 15+, e as chaves `NSBluetoothAlwaysUsageDescription`
e `NSLocalNetworkUsageDescription` (+ `NSBonjourServices`) no Info.plist.

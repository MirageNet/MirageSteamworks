## [2.0.2](https://github.com/MirageNet/MirageSteamworks/compare/v2.0.1...v2.0.2) (2026-01-07)


### Bug Fixes

* adding queues for steam callback and dequeueing inside tick ([289e008](https://github.com/MirageNet/MirageSteamworks/commit/289e008c4ee09e7d98ba633d7283f72710e911f3))

## [2.0.1](https://github.com/MirageNet/MirageSteamworks/compare/v2.0.0...v2.0.1) (2026-01-07)


### Bug Fixes

* reworking client connect to make sure events are called within ReceiveData ([c20bbe0](https://github.com/MirageNet/MirageSteamworks/commit/c20bbe0eca5efcf200ced79edc0e6993f2b36625))

# [2.0.0](https://github.com/MirageNet/MirageSteamworks/compare/v1.2.1...v2.0.0) (2026-01-04)


* feat!: performance improvements from Mirage v156 ([11eb7ec](https://github.com/MirageNet/MirageSteamworks/commit/11eb7ecc217e77d8740e44c3a0243bc12519c77a))


### BREAKING CHANGES

* increasing minimum mirage version to v156.2.0

## [1.2.1](https://github.com/MirageNet/MirageSteamworks/compare/v1.2.0...v1.2.1) (2025-12-18)


### Bug Fixes

* improving handling of ConnectionRejected and better disconnect reason ([d9a860e](https://github.com/MirageNet/MirageSteamworks/commit/d9a860e65cf33437e884ffbfdc45342454b9c0cc))

# [1.2.0](https://github.com/MirageNet/MirageSteamworks/compare/v1.1.0...v1.2.0) (2025-12-18)


### Bug Fixes

* using steam SteamNetConnectionEnd enum for close ([f2dc192](https://github.com/MirageNet/MirageSteamworks/commit/f2dc192a96da098580a16bb5239c728484cb2527))


### Features

* adding AcceptConnectionCallback to allow connections to be rejected ([11c565d](https://github.com/MirageNet/MirageSteamworks/commit/11c565df98b3891fe252d3a873b1766328c0b12b))

# [1.1.0](https://github.com/MirageNet/MirageSteamworks/compare/v1.0.4...v1.1.0) (2025-09-19)


### Features

* adding SteamNetworkingIdentity to public field ([a208e42](https://github.com/MirageNet/MirageSteamworks/commit/a208e424f3a0bee1f5cf58a8b0498b119fa74db9))

## [1.0.4](https://github.com/MirageNet/MirageSteamworks/compare/v1.0.3...v1.0.4) (2025-09-12)


### Bug Fixes

* lowering default maxPacketSize ([e8ab182](https://github.com/MirageNet/MirageSteamworks/commit/e8ab1827dd69625e1677278d2140897c670cbe23))

## [1.0.3](https://github.com/MirageNet/MirageSteamworks/compare/v1.0.2...v1.0.3) (2025-09-12)


### Bug Fixes

* fixing max size of steam messages for unreliable channel ([ae8b637](https://github.com/MirageNet/MirageSteamworks/commit/ae8b637b5b6e0859396f81057d93399ea99a93d8))
* passing max size from socketFactory to Steamworks ([65547f4](https://github.com/MirageNet/MirageSteamworks/commit/65547f4758ec1565a567631f5f5087b22cbaf293))
* removing unneeded error logs ([d1713e8](https://github.com/MirageNet/MirageSteamworks/commit/d1713e8b679caa1eb0a71da63e3aef601f48994c))
* using NoDelay for Unreliable, which includes noNagle ([ce80346](https://github.com/MirageNet/MirageSteamworks/commit/ce80346b25249259b8ed002eadfb502df520fcc7))
* using noNagle for sends ([43bdb5e](https://github.com/MirageNet/MirageSteamworks/commit/43bdb5e3e2750a47b2948f731363951108a3b3a9))

## [1.0.2](https://github.com/MirageNet/MirageSteamworks/compare/v1.0.1...v1.0.2) (2025-08-09)


### Bug Fixes

* cleaning up client logic and fixing nullref on timeout ([4ccb242](https://github.com/MirageNet/MirageSteamworks/commit/4ccb242b1265750dbd0d6a310c3b0b7e6bf8c800))
* using CreateGameServer for server ([e055350](https://github.com/MirageNet/MirageSteamworks/commit/e05535089a353ee7976c91987a40057185d15889))

## [1.0.1](https://github.com/MirageNet/MirageSteamworks/compare/v1.0.0...v1.0.1) (2025-07-02)


### Bug Fixes

* adding missing meta file ([ec08ada](https://github.com/MirageNet/MirageSteamworks/commit/ec08adaee84bf05acc1112d19bdab12007d34f3b))

# 1.0.0 (2025-07-02)


### Features

* v1 releasee ([2fd23db](https://github.com/MirageNet/MirageSteamworks/commit/2fd23db8f68c6e4b69ab70c82eb5fe998eb51cfb))

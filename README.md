[![Discord](https://img.shields.io/discord/809535064551456888.svg)](https://discordapp.com/invite/yp6W73Xs68)
[![release](https://img.shields.io/github/release/MirageNet/MirageSteamworks.svg)](https://github.com/MirageNet/MirageSteamworks/releases/latest)
[![openupm](https://img.shields.io/npm/v/com.miragenet.steamworkssocket?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.miragenet.steamworkssocket/)

# Mirage Steamworks

Steamworks.NET Transport for [Mirage](https://github.com/MirageNet/Mirage).

Dependencies:
- [Mirage](https://github.com/MirageNet/Mirage)
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)
- [SteamManager](https://github.com/rlabrecque/Steamworks.NET-SteamManager) (optional) (see [docs](https://steamworks.github.io/steammanager/))

When installing via package manager `Mirage` and `Steamworks.NET` will be automatically included by unity, but the `SteamManager.cs` is something you will need to include in your project manually


## Installation

#### Install via OpenUPM Package manager gui

1) Add openupm registry.  Click on the menu Edit -> Project settings...,  and add a scoped registry like so: <br/>
    Name: `OpenUPM` <br/>
    Url: `https://package.openupm.com` <br/>
    Scopes:
        - `com.miragenet`
        - `com.cysharp.unitask`
        - `com.rlabrecque.steamworks.net`
2) Close the project settings
3) Open the package manager.  
4) Click on menu Window -> Package Manager and select "Packages: My Registries", 
5) select the latest version of `Mirage Steamworks.net Socket` and click install, like so:
6) You may come back to the package manager to unistall `Mirage Steamworks.net Socket` or upgrade it.

#### Install via OpenUPM manifest.json

add `com.miragenet.steamworkssocket` to your unity `manifest.json` file, make sure to use latest versions 

```json
{
  "dependencies": {
    "com.miragenet.mirage": "156.2.0",
    "com.rlabrecque.steamworks.net": "20.2.0",
    "com.cysharp.unitask": "2.2.5",
    "com.miragenet.steamworkssocket": "1.0.0",
    // ...   
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.miragenet",
        "com.cysharp.unitask",
        "com.rlabrecque.steamworks.net"
      ]
    }
  ]
}
```

#### Install via git url

add `com.miragenet.steamworkssocket` to your unity `manifest.json` file, and set git url. Make sure to set version tag

```json
{
  "dependencies": {
    "com.miragenet.steamworkssocket": "https://github.com/MirageNet/MirageSteamworks.git?path=/Assets/MirageSteamworks#v1.0.0",
    // ...   
  }
}
```

#### Install manually 

Download code from this repo or [Release page](https://github.com/MirageNet/MirageSteamworks/releases) and add the `Assets/MirageSteamworks` folder to your project

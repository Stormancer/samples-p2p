Stormancer P2P Sample
=====================

The sample is a simple, fully P2P console based chat program that uses the Stormancer library and servers for authentication and NAT traversal.

The application:

- Authenticates with the Stormancer server app using the deviceidentifier authentication provider
- Finds/creates a chat room using a very simple game finding policy
- Establishs a P2P connection between members of the chatroom
- Broadcast text messages between all chat participants.

Building the sample
===================
The sample requires the 1.6 Stormancer library, available there:
https://github.com/Stormancer/stormancer-sdk-cpp/releases/tag/v1.6

The include an libs directory of the library must be copied in client-cpp/stormancer (the resulting directories must be client-cpp/stormancer/libs and client-cpp/stormancer/include

Run Visual studio and build client-cpp either in release or debug. Please note that we only ship x64 libraries, but x86 versions could be built from https://github.com/Stormancer/stormancer-sdk-cpp .

Usage
=====

client-cpp.exe <userName> <chatRoomId>

chatRoomId is restricted to alphanumeric characters, - and _ .

Server application config
=========================

    {
		"serviceLocator": {
			"defaultMapping": {
				"stormancer.authenticator": "authenticator",
				"stormancer.plugins.gamefinder": "gamefinder-{ServiceName}"
			}
		},
		"gameSession": {
			"usep2p": true
		},
		"gamefinder": {
			"rules": {
				"filters": [
					{
						"config": "default"
					}
				],
				"configs": {
					"default": {
						"totalPlayers": 10000
					}
				}
			},
			"configs": {
				"default": {
					"teamSize": 1,
					"readyCheck": {
						"enabled": false,
						"timeout": 10
					},
					"interval": 1
				}
			}
		}
	}
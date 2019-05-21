#pragma once

#include "stormancer/RPC/Service.h"
#include "stormancer/Event.h"
#include "stormancer/Action.h"
#include "GameSession/GameSessionModels.h"

namespace Stormancer
{
	///
	/// Const var
	const std::string GAMESESSION_P2P_SERVER_ID = "GameSession";

	///
	/// Forward declare
	class Scene;
	class P2PTunnel;

	class GameSessionService : public std::enable_shared_from_this<GameSessionService>
	{
	public:

		GameSessionService(std::weak_ptr<Scene> scene);
		~GameSessionService();

		void initialize();

		pplx::task<void> initializeTunnel(std::string p2pToken, pplx::cancellation_token ct);

		pplx::task<void> waitServerReady(pplx::cancellation_token);

		std::vector<SessionPlayer> getConnectedPlayers();

		pplx::task<std::string> getUserFromBearerToken(std::string token);

		pplx::task<Packetisp_ptr> sendGameResults(const StreamWriter& streamWriter, pplx::cancellation_token ct = pplx::cancellation_token::none());

		pplx::task<std::string> p2pTokenRequest(pplx::cancellation_token ct);

		pplx::task<void> reset(pplx::cancellation_token ct);

		pplx::task<void> disconnect();

		bool shouldEstablishTunnel = true;

		std::weak_ptr<Scene> getScene();

		void onDisconnecting();

		void ready(std::string data);

		void onP2PConnected(std::function<void(std::shared_ptr<Stormancer::IP2PScenePeer>)> callback);

		void onConnectionFailure(std::function<void(std::string)> callback);

		Event<void> onAllPlayerReady;
		Event<std::string> onRoleReceived;
		Event<std::shared_ptr<Stormancer::P2PTunnel>> onTunnelOpened;
		Event<void> onShutdownReceived;
		Event<SessionPlayer, std::string> onPlayerChanged;

	private:

		pplx::cancellation_token linkTokenToDisconnection(pplx::cancellation_token tokenToLink);

		std::shared_ptr<P2PTunnel> _tunnel;
		Action<std::string> _onConnectionFailure;
		std::function<void(std::shared_ptr<Stormancer::IP2PScenePeer>)> _onConnectionOpened;
		pplx::task_completion_event<void> _waitServerTce;

		std::weak_ptr<Scene> _scene;
		std::vector<SessionPlayer> _users;
		std::shared_ptr<Stormancer::ILogger> _logger;
		bool _receivedP2PToken = false;
		pplx::cancellation_token_source _disconnectionCts;
	};
}
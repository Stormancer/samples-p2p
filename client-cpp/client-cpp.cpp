// client-cpp.cpp : Ce fichier contient la fonction 'main'. L'exécution du programme commence et se termine à cet endroit.
//

#include "pch.h"
#include <iostream>
#include <thread>
#include <chrono>

//Stormancer includes
#include "stormancer/IClient.h"
#include "stormancer/Exceptions.h"
#include "stormancer/Logger/ConsoleLoggerWindows.h"

//Plugins
#include "Users/Users.hpp"
#include "GameFinder/GameFinder.hpp"
#include "GameSession/Gamesessions.hpp"




using namespace std::chrono_literals;

//A structure used to send custom game finding parameters to the server
struct GameFinderParameters
{
	std::string gameId;
	MSGPACK_DEFINE(gameId)
};

//Gamefinding an connection logic to the game session
bool sample_p2p(std::shared_ptr<Stormancer::IClient> client,std::string userId, std::string gameId)
{
	auto gameFinder = client->dependencyResolver().resolve<Stormancer::GameFinder::GameFinderApi>();
	auto gameSession = client->dependencyResolver().resolve<Stormancer::GameSessions::GameSession>();

	auto gameFoundTask = gameFinder->waitGameFound();
	GameFinderParameters p;
	p.gameId = gameId;
	//Start the game query
	gameFinder->findGame("default", "p2p-sample", p).get();

	//wait to find a game.
	auto gameFound = gameFoundTask.get();



	//Add code that's going to be run when we want to initialize the gamesession scene (mainly register route handlers...)
	//This code is going to be called before actual connection happens.
	auto initSubscription = gameSession->onConnectingToScene.subscribe([](std::shared_ptr<Stormancer::Scene> gs) {

		//Register a P2P route
		gs->addRoute("hello", [](Stormancer::Packetisp_ptr packet) {
			std::cout << packet->readObject<std::string>() << std::endl;
		}, Stormancer::MessageOriginFilter::Peer);

	});



	//Connect to the game session and establish a P2P connectivity with the host if necessary.
	auto connectionInfos = gameSession->connectToGameSession(gameFound.data.connectionToken).get();
	if (connectionInfos.isHost)
	{
		std::cout << "Starting as host";
		//Start game server on the port specified in config->serverGamePort
	}
	else
	{
		std::cout << "Starting as client";

		//connect the game client to connectionInfos.endpoint
	}
	//Indicates that the game is ready. This is necessary because the host indicates by calling this function 
	//that it's ready to accept connection from other game clients.
	gameSession->setPlayerReady().get();

	Stormancer::Serializer serializer;

	//Wait for user input and broadcast it to all the other peers in P2P.
	std::cout << std::endl;
	while (true)
	{
		
		std::string input;
		std::getline(std::cin,input);
		input = userId + ": " + input;
		//Broadcast a message to all other P2P peers
		gameSession->scene()->send(Stormancer::PeerFilter::matchAllP2P(), "hello", [serializer,input](Stormancer::obytestream& stream) {
			serializer.serialize(stream, input);
		});

		std::this_thread::sleep_for(20ms);
	}

	return connectionInfos.isHost;
}

//Gamefinding and connection logic to the game session, using the task based asynchronous pattern.
pplx::task<void> sample_p2p_async(std::shared_ptr<Stormancer::IClient> client, std::string gameId)
{
	std::weak_ptr<Stormancer::IClient> wClient = client;
	auto gameFinder = client->dependencyResolver().resolve<Stormancer::GameFinder::GameFinderApi>();


	auto gameFoundTask = gameFinder->waitGameFound();
	GameFinderParameters p;
	p.gameId = gameId;
	//Start the game query
	return gameFinder->findGame("default", "p2p-sample", p)
		.then([gameFoundTask]() {

		//Return the gameFound task so that the next continuation runs when this task completes.
		return gameFoundTask;
	})

		.then([wClient](Stormancer::GameFinder::GameFoundEvent gameFound) {
		//We capture a weak pointer so that we don't prevent the client from getting destroyed if the program decides so, 
		//because we are running asynchronously, anything may happen.
		auto client = wClient.lock();
		if (!client)
		{
			throw Stormancer::PointerDeletedException();
		}

		auto gameSession = client->dependencyResolver().resolve<Stormancer::GameSessions::GameSession>();


		return gameSession->connectToGameSession(gameFound.data.connectionToken);

	})
		.then([wClient](Stormancer::GameSessions::GameSessionConnectionParameters connectionInfos) {
		//We capture a weak pointer so that we don't prevent the client from getting destroyed if the program decides so, 
		//because we are running asynchronously, anything may happen.
		auto client = wClient.lock();
		if (!client)
		{
			throw Stormancer::PointerDeletedException();
		}

		if (connectionInfos.isHost)
		{
			std::cout << "Starting as host";
			//Start game server on the port specified in config->serverGamePort
		}
		else
		{
			std::cout << "Starting as client";

			//connect the game client to connectionInfos.endpoint
		}

		auto gameSession = client->dependencyResolver().resolve<Stormancer::GameSessions::GameSession>();

		//Indicates that the game is ready. This is necessary because the host indicates by calling this function 
		//that it's ready to accept connection from other game clients.
		gameSession->setPlayerReady().get();

	});
}




int main(int argc, char** argv)
{
	if (argc < 3)
	{
		std::cout << "usage: client-cpp {userId} {gameId} \n";
		std::cout << "userId : Id of the user in the game. The sample uses this identifier (no authentication)\n";
		std::cout << "gameId : Id of the game the client is going to join.\n";
		return -1;
	}

	//Create a configuration object to connect to the application samples/p2p on the test server gc3.stormancer.com
	auto config = Stormancer::Configuration::create("http://gc3.stormancer.com", "samples", "p2p");
	//Set the port used by the game server.
	config->serverGamePort = 7777;
	//Add the plugins required to create a P2P application.
	config->addPlugin(new Stormancer::Users::UsersPlugin());
	config->addPlugin(new Stormancer::GameFinder::GameFinderPlugin());
	config->addPlugin(new Stormancer::GameSessions::GameSessionsPlugin());
	
	//Uncomment to get detailed logging
	config->logger = std::make_shared<Stormancer::ConsoleLogger>();

	//Create a stormancer client
	auto client = Stormancer::IClient::create(config);

	
	std::string userId = argv[1];
	std::string gameId = argv[2];

	//Setup the authentication system to use the deviceidentifier provider with the userId provider as cmdline arg.
	auto auth = client->dependencyResolver().resolve<Stormancer::Users::UsersApi>();
	auth->getCredentialsCallback = [userId]() {

		Stormancer::Users::AuthParameters p;
		p.type = "deviceidentifier";
		p.parameters.emplace("deviceidentifier", userId);
		return pplx::task_from_result(p);
	};

	auto sub = auth->connectionStateChanged.subscribe([](Stormancer::Users::GameConnectionState state) {
		std::cout << "Game connection state changed : " << state << std::endl;
	});


	bool isHost = sample_p2p(client,userId, gameId);



	//sample_p2p_async( client,gameId).get();
	return 0;
}


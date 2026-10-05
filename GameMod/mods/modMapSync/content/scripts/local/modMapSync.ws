// modMapSync: writes the game's map pins (with the "discovered" flag) and the tracked trackedQuest
// into scriptslog.txt, where the Witcher 3 Dynamic Map app picks them up.
//
// Uses the script annotations of the next-gen game (4.00+), so Script Merger is not needed.
// The game must be started with the -debugscripts launch option, otherwise LogChannel
// writes nothing. The log is Documents\The Witcher 3\scriptslog.txt.

@addField(CR4Player)
var mapSyncHash : string;

@addField(CR4Player)
var mapSyncLastPos : Vector;

@addField(CR4Player)
var mapSyncIdleTicks : int;

@wrapMethod(CR4Player)
function OnSpawned( spawnData : SEntitySpawnData )
{
	wrappedMethod( spawnData );

	mapSyncHash = "";
	mapSyncIdleTicks = 0;
	AddTimer( 'MapSyncTimer', 3.0, true );
	AddTimer( 'MapSyncPosTimer', 0.1, true );
}

// The player position (on foot, on a horse, in a boat) is written to the log 10 times a second,
// but only while the player moves; a standing player is written once a second as a heartbeat.
// This keeps the log small and the app does not need to read the game's memory.
@addMethod(CR4Player)
timer function MapSyncPosTimer( dt : float, id : int )
{
	var pos : Vector;

	pos = GetWorldPosition();
	mapSyncIdleTicks += 1;

	if ( VecDistance( pos, mapSyncLastPos ) < 0.2 && mapSyncIdleTicks < 10 )
	{
		return;
	}

	mapSyncLastPos = pos;
	mapSyncIdleTicks = 0;
	LogChannel( 'MapSync', "@@MS|L|" + FloatToStringPrec( pos.X, 1 ) + "|"
		+ FloatToStringPrec( pos.Y, 1 ) + "|" + FloatToStringPrec( pos.Z, 1 ) );
}

@addMethod(CR4Player)
timer function MapSyncTimer( dt : float, id : int )
{
	var worldPath, questTitle, hash : string;
	var pins : array< SCommonMapPinInstance >;
	var pin : SCommonMapPinInstance;
	var i, discovered, known : int;
	var journal : CWitcherJournalManager;
	var trackedQuest : CJournalQuest;

	worldPath = theGame.GetWorld().GetDepotPath();

	journal = theGame.GetJournalManager();
	trackedQuest = journal.GetTrackedQuest();
	if ( trackedQuest )
	{
		questTitle = GetLocStringById( trackedQuest.GetTitleStringId() );
	}

	pins = theGame.GetCommonMapManager().GetMapPinInstances( worldPath );

	for ( i = 0; i < pins.Size(); i += 1 )
	{
		if ( pins[i].isDiscovered )
		{
			discovered += 1;
		}
		if ( pins[i].isKnown )
		{
			known += 1;
		}
	}

	// The snapshot is written only when something changed, so the log does not grow every 3 seconds.
	hash = worldPath + "/" + pins.Size() + "/" + discovered + "/" + known + "/" + questTitle;
	if ( hash == mapSyncHash )
	{
		return;
	}
	mapSyncHash = hash;

	LogChannel( 'MapSync', "@@MS|B|" + worldPath + "|" + questTitle );

	for ( i = 0; i < pins.Size(); i += 1 )
	{
		pin = pins[i];
		LogChannel( 'MapSync', "@@MS|P|" + NameToString( pin.type ) + "|"
			+ FloatToStringPrec( pin.position.X, 1 ) + "|"
			+ FloatToStringPrec( pin.position.Y, 1 ) + "|"
			+ FloatToStringPrec( pin.position.Z, 1 ) + "|"
			+ (int)pin.isDiscovered + "|" + (int)pin.isKnown );
	}

	LogChannel( 'MapSync', "@@MS|E|" + pins.Size() );
}

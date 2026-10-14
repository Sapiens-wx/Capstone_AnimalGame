/*
 * Author delivered M2 actions in the external FMOD Studio 2.03 project.
 * Back up the complete Studio project before running this script. It imports
 * source files without processing them, preserves existing event GUIDs, and
 * saves only after validating the requested events. Build Desktop separately.
 */
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var sourceRoot = "C:/Users/PC/Capstone_AnimalGame/FMOD/SourceAudio/Milestone2/PlayerActions/";
    var workspace = studio.project.workspace;
    var definitions = [
        { path: "event:/Robot/Move/Start", file: "Movement/M01_Move_Start.wav" },
        { path: "event:/Robot/Move/Drive", file: "Movement/M02_Move_Drive_Loop.wav", loopStart: 3.5, loopEnd: 4.25, crossfade: 0.08 },
        { path: "event:/Robot/Move/Acceleration", file: "Movement/M02_Move_Drive_Loop.wav", trimEnd: 3.75 },
        { path: "event:/Robot/Move/Slide", file: "Movement/M06_Move_LoseGrip.wav", loopStart: 1.23, loopEnd: 2.47, holdName: "SlideHold" },
        { path: "event:/Robot/Move/Airborne", file: "Movement/M07_MidAir_short_1.wav" },
        { path: "event:/Robot/Move/Land", file: "Movement/M08_Land_Impact.wav" },
        { path: "event:/Robot/Move/LandParts", file: "Movement/M08_Land_shatter.wav" },
        { path: "event:/Robot/Move/Ground", file: "Movement/M_Move_Ground_Default.wav", loopStart: 1, crossfade: 0.04 },
        { path: "event:/Robot/Move/GroundTurn", file: "Movement/M_Move_Ground_turn.wav", loopStart: 0.81, loopEnd: 3.87, crossfade: 0.04 },
        { path: "event:/Robot/Arm/Deploy", file: "Arm/A01_Arm_Deploy_Loop.wav", loopStart: 0.35, loopEnd: 0.8, holdName: "DeployHold" },
        { path: "event:/Robot/Arm/Retract", file: "Arm/A01_Arm_Deploy_Loop.wav", loopStart: 0.35, loopEnd: 0.8, holdName: "RetractHold" },
        { path: "event:/Robot/Arm/Blocked", file: "Arm/A03_Arm_Blocked_01.wav" }
    ];
    var preservedPaths = [
        "event:/Robot/Scan/Charge", "event:/Robot/Scan/Pulse",
        "event:/Robot/Camera/Open", "event:/Robot/Camera/Close",
        "event:/Robot/Camera/Move", "event:/Robot/Camera/Focus", "event:/Robot/Camera/Shutter"
    ];
    var preservedIds = {};
    var imported = {};

    function requireCondition(value, message) {
        if (!value) throw new Error("Milestone 2 authoring: " + message);
    }
    function closeEnough(a, b) { return Math.abs(a - b) < 0.000001; }
    function exactType(object, type) { return object && object.isOfExactType(type); }
    function findFolder(name, parent) {
        var matches = studio.project.model.EventFolder.findInstances().filter(function (item) {
            return item.name === name && item.folder && item.folder.id === parent.id;
        });
        requireCondition(matches.length <= 1, "Duplicate folder " + name);
        if (matches.length) return matches[0];
        var folder = studio.project.create("EventFolder");
        folder.name = name;
        folder.folder = parent;
        return folder;
    }
    function normalizeParameter(parameter) {
        if (exactType(parameter, "ParameterProxy")) return parameter.preset;
        if (exactType(parameter, "ParameterPreset")) return parameter.parameter;
        return parameter;
    }
    function configureHold(event, loop, holdName) {
        var matches = event.getParameterPresets().map(normalizeParameter).filter(function (parameter) {
            if (!parameter.presetOwner) return false;
            var name = parameter.presetOwner.name;
            return name === holdName || /^Hold(?: ?\(\d+\))?$/.test(name);
        });
        requireCondition(matches.length <= 1, "Duplicate sustain parameter in " + event.getPath());
        var hold = matches.length ? matches[0] : normalizeParameter(event.addGameParameter({
            name: holdName, type: studio.project.parameterType.User, min: 0, max: 1
        }));
        requireCondition(exactType(hold, "GameParameter"), "Hold must be a game parameter");
        // Studio parameter preset names are unique across the project. Reuse
        // the initial Hold / Hold (N) objects so existing condition IDs survive.
        hold.presetOwner.name = holdName;
        hold.minimum = 0;
        hold.maximum = 1;
        hold.initialValue = 1;
        hold.isGlobal = false;
        hold.isReadOnly = false;
        var conditions = loop.triggerConditions;
        requireCondition(conditions.length <= 1, "Unexpected extra loop conditions in " + event.getPath());
        var condition = conditions.length ? conditions[0] : loop.addParameterCondition(hold, 0.5, 1);
        requireCondition(exactType(condition, "ParameterCondition") && condition.parameter.id === hold.id,
            "Unexpected condition on " + event.getPath());
        condition.minimum = 0.5;
        condition.maximum = 1;
        requireCondition(hold.isValid && condition.isValid, "Invalid Hold loop condition");
    }
    function configureCrossfade(loop, sound, seconds) {
        var transition = loop.transitionTimeline;
        if (!transition) {
            transition = studio.project.create("TransitionTimeline");
            transition.event = loop;
        }
        transition.length = seconds;
        function proxy(type) {
            var matches = transition.modules.filter(function (module) { return exactType(module, type); });
            requireCondition(matches.length <= 1, "Duplicate transition proxy " + type);
            var result = matches.length ? matches[0] : studio.project.create(type);
            result.audioTrack = sound.audioTrack;
            result.parameter = transition;
            result.start = 0;
            result.length = seconds;
            result.isAsync = true;
            return result;
        }
        var source = proxy("TransitionSourceSound");
        var destination = proxy("TransitionDestinationSound");
        requireCondition(transition.modules.length === 2, "Unexpected transition content");
        function fade(owner, related, fadeIn) {
            var type = fadeIn ? "TransitionDestinationFadeInCurve" : "TransitionSourceFadeOutCurve";
            var relation = fadeIn ? "fadeInCurve" : "fadeOutCurve";
            var curve = owner[relation];
            requireCondition(!curve || exactType(curve, type), "Unexpected transition fade type");
            if (!curve) {
                curve = studio.project.create(type);
                owner[relation] = curve;
                curve.startPoint = studio.project.create("AutomationPoint");
                curve.endPoint = studio.project.create("AutomationPoint");
            }
            curve.relatedModule = related;
            curve.startPoint.position = 0;
            curve.startPoint.value = fadeIn ? 0 : 1;
            curve.startPoint.curveShape = fadeIn ? -0.2547189 : 0.25471893;
            curve.endPoint.position = seconds;
            curve.endPoint.value = fadeIn ? 1 : 0;
            requireCondition(curve.isValid && owner.isValid, "Invalid transition fade");
        }
        fade(source, destination, false);
        fade(destination, source, true);
        requireCondition(transition.isValid, "Invalid loop transition");
    }

    requireCondition(String(studio.project.filePath).replace(/\\/g, "/").toLowerCase() === expectedProject,
        "Refusing to modify a different Studio source project");
    requireCondition(studio.version.productVersion === 2 && studio.version.majorVersion === 3,
        "Requires the FMOD Studio 2.03 scripting API");
    studio.project.model.Event.findInstances();
    preservedPaths.forEach(function (path) {
        var event = studio.project.lookup(path);
        requireCondition(event && event.isValid, "Missing preserved M1 event " + path);
        preservedIds[path] = event.id;
    });
    definitions.forEach(function (definition) {
        requireCondition(studio.system.getFile(sourceRoot + definition.file).exists(), "Missing source " + definition.file);
    });

    var robot = findFolder("Robot", workspace.masterEventFolder);
    var moveFolder = findFolder("Move", robot);
    var armFolder = findFolder("Arm", robot);
    var banks = studio.project.model.Bank.findInstances().filter(function (bank) { return bank.name === "RobotActions"; });
    requireCondition(banks.length <= 1, "Duplicate RobotActions banks");
    var bank = banks.length ? banks[0] : studio.project.create("Bank");
    requireCondition(!bank.isMasterBank, "RobotActions cannot replace the master bank");
    bank.name = "RobotActions";
    bank.folder = workspace.masterBankFolder;

    definitions.forEach(function (definition) {
        var asset = imported[definition.file];
        if (!asset) {
            asset = studio.project.importAudioFile(sourceRoot + definition.file);
            requireCondition(asset && asset.isValid && asset.length > 0, "Import failed for " + definition.file);
            imported[definition.file] = asset;
        }
        var trimStart = definition.trimStart === undefined ? 0 : definition.trimStart;
        var trimEnd = definition.trimEnd === undefined ? asset.length : definition.trimEnd;
        var soundLength = trimEnd - trimStart;
        requireCondition(trimStart >= 0 && soundLength > 0 && trimEnd <= asset.length + 0.000001,
            "Requested source window exceeds " + definition.file + " (" + asset.length + " s)");
        var event = studio.project.lookup(definition.path);
        var previousId = event ? event.id : null;
        if (!event) event = workspace.addEvent(definition.path.substring(definition.path.lastIndexOf("/") + 1), false);
        requireCondition(event && event.isValid, "Invalid event " + definition.path);
        event.folder = definition.path.indexOf("/Arm/") !== -1 ? armFolder : moveFolder;
        requireCondition(event.banks.length === 0 || (event.banks.length === 1 && event.banks[0].id === bank.id),
            "Existing event belongs to another bank: " + definition.path);
        if (!event.banks.length) event.relationships.banks.add(bank);
        var tracks = event.groupTracks;
        requireCondition(tracks.length <= 1, "Refusing to replace extra tracks in " + definition.path);
        var track = tracks.length ? tracks[0] : event.addGroupTrack("Audio");
        var sounds = event.timeline.modules.filter(function (module) { return exactType(module, "SingleSound"); });
        requireCondition(sounds.length <= 1 && event.timeline.modules.length === sounds.length,
            "Refusing to replace complex timeline content in " + definition.path);
        var sound = sounds.length ? sounds[0] : track.addSound(event.timeline, "SingleSound", 0, soundLength);
        sound.audioFile = asset;
        sound.start = 0;
        sound.length = soundLength;
        sound.isAsync = false;
        sound.looping = false;
        // Studio Start Offset uses percent of the original asset, not seconds.
        sound.startOffset = 100 * trimStart / asset.length;

        var loops = studio.project.model.LoopRegion.findInstances().filter(function (region) {
            return region.timeline && region.timeline.id === event.timeline.id;
        });
        var looping = definition.loopStart !== undefined;
        requireCondition(loops.length <= (looping ? 1 : 0), "Unexpected loop regions in " + definition.path);
        if (looping) {
            var loopEnd = definition.loopEnd === undefined ? soundLength : definition.loopEnd;
            requireCondition(loopEnd > definition.loopStart && loopEnd <= soundLength + 0.000001,
                "Loop exceeds authored source window in " + definition.path);
            var markerTrack = event.markerTracks.length ? event.markerTracks[0] : event.addMarkerTrack();
            var loop = loops.length ? loops[0] : markerTrack.addRegion(definition.loopStart,
                loopEnd - definition.loopStart, "Loop", studio.project.regionLoopMode.Looping);
            loop.position = definition.loopStart;
            loop.length = loopEnd - definition.loopStart;
            if (definition.holdName) configureHold(event, loop, definition.holdName);
            else requireCondition(loop.triggerConditions.length === 0, "Unexpected loop conditions in " + definition.path);
            if (definition.crossfade) configureCrossfade(loop, sound, definition.crossfade);
            requireCondition(loop.isValid && closeEnough(loop.position + loop.length, loopEnd), "Invalid loop region");
        }
        requireCondition(sound.isValid && !event.is3D(), "Invalid or non-2D event " + definition.path);
        requireCondition(event.isOneShot() === !looping, "Unexpected one-shot state in " + definition.path);
        requireCondition(!previousId || previousId === event.id, "Existing M2 event GUID changed");
        console.log("M2_EVENT " + JSON.stringify({ path: definition.path, guid: event.id, file: definition.file,
            sourceSeconds: asset.length, startOffsetPercent: sound.startOffset, soundSeconds: sound.length,
            loopStart: definition.loopStart, loopEnd: definition.loopEnd === undefined && looping ? soundLength : definition.loopEnd,
            hold: definition.holdName || null, crossfade: definition.crossfade || 0 }));
    });
    preservedPaths.forEach(function (path) {
        requireCondition(studio.project.lookup(path).id === preservedIds[path], "Preserved M1 GUID changed: " + path);
    });
    requireCondition(studio.project.save(), "Saving the M2 source project failed");
    console.log("M2_READY " + definitions.length + " delivered 2D events in RobotActions; all seven M1 GUIDs retained.");
}());

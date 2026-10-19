/*
 * Author delivered M3 balance/recycling sources in the external FMOD 2.03 project.
 * Back up the complete Studio project first. Sync replacements byte-for-byte to
 * its flat Assets directory and refresh existing AudioFile metadata before use.
 * No source audio, earlier event, master mix or bank build is changed here.
 * Author only present deliveries; log missing sources without substitutes and
 * preserve any previously authored events whose delivery is absent this time.
 * New M3 deliveries keep their full source/intro. Explicitly approved M2 reuse
 * can select an instrument window without changing the WAV. Reviewed loop markers
 * avoid exported silence and separate closing/impact tails. Parameter mix curves
 * remain unset. Save only; build Desktop and bind Unity GUIDs separately.
 */
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var sourceRoot = "C:/Users/PC/Capstone_AnimalGame/FMOD/SourceAudio/Milestone3/PlayerActions/";
    var reusedSourceRoot = "C:/Users/PC/Capstone_AnimalGame/FMOD/SourceAudio/Milestone2/PlayerActions/";
    var bankName = "RobotFoley";
    var parameterKinds = ["Speed", "Progress", "Effort", "Impact"];
    var definitions = [
        // B01 reuses the existing M2 Slide event with a runtime pitch adjustment.
        // No separate B01 source is required; any older TumbleStart event survives.
        { path: "event:/Robot/Balance/TumbleMove", file: "Balance/B02_Tumble_Move_Loop.wav", loop: true,
            loopStart: 0.70, loopEnd: 2.04, crossfade: 0.04 },
        // Runtime chooses between 01 and 03 in a shared exclusive voice.
        // The corner-impact variant 02 is retired; any older event survives.
        { path: "event:/Robot/Balance/TumbleImpact", file: "Balance/B03_Tumble_Impact_01.wav" },
        { path: "event:/Robot/Balance/TumbleImpact03", file: "Balance/B03_Tumble_Impact_03.wav" },
        { path: "event:/Robot/Balance/RockImpact", file: "Balance/B04_Tumble_Rock_Impact.wav" },
        { path: "event:/Robot/Balance/RecoveredUpright", file: "Balance/B05_Tumble_Recovered.wav" },
        { path: "event:/Robot/Balance/SettledFallen", file: "Balance/B06_Tumble_Settled.wav" },
        { path: "event:/Robot/Balance/SupportStart", file: "Balance/B07_Support_Start.wav" },
        { path: "event:/Robot/Balance/SupportLoad", file: "Balance/B07_Support_Load_Loop.wav", loop: true,
            loopStart: 0.25, loopEnd: 1.25, crossfade: 0.05 },
        // Explicitly approved reuse: only new event instrument windows/fades;
        // never alter these M2 source assets or their existing M2 events.
        { path: "event:/Robot/Balance/ForceAccepted", file: "Arm/A02_Arm_Servo_Loop.wav", sourceRoot: reusedSourceRoot,
            trimStart: 0.25, trimEnd: 0.40, fadeIn: 0.01, fadeOut: 0.02 },
        { path: "event:/Robot/Balance/ForceRejected", file: "Arm/A03_Arm_Blocked_01.wav", sourceRoot: reusedSourceRoot },
        { path: "event:/Robot/Balance/PullBalance", file: "Arm/A02_Arm_Servo_Loop.wav", sourceRoot: reusedSourceRoot,
            loop: true, loopStart: 0.05, loopEnd: 0.65, crossfade: 0.04 },
        { path: "event:/Robot/Balance/SupportFailed", file: "Arm/A01_Arm_Deploy_Loop.wav", sourceRoot: reusedSourceRoot,
            trimStart: 0.80 },
        { path: "event:/Robot/Balance/RightingChassis", file: "Balance/B12_Righting_Chassis_Loop.wav", loop: true,
            loopStart: 0.30, loopEnd: 1.45, crossfade: 0.05 },
        { path: "event:/Robot/Balance/RightingLand", sourceRoot: reusedSourceRoot, layers: [
            { file: "Movement/M08_Land_Impact.wav", track: "Impact" },
            { file: "Movement/M08_Land_shatter.wav", track: "Parts" }
        ] },
        // No reliable public gameplay hook yet. Optional delivery does not imply
        // a runtime trigger; missing reserved sources do not block active cues.
        { path: "event:/Robot/Recycle/PushStart", file: "Recycling/G01_Push_Start.wav", reserved: true },
        { path: "event:/Robot/Recycle/PushLoop", file: "Recycling/G01_Push_Loop.wav", loop: true, reserved: true },
        // Unified intake starts at source zero. Its crossfade reads only through
        // 0.58 s; runtime ends RecycleHold and seeks to the closing tail at 0.80 s.
        { path: "event:/Robot/Recycle/Intake", file: "Recycling/G03_Recycle.wav", loop: true,
            loopStart: 0.20, loopEnd: 0.54, crossfade: 0.04, holdName: "RecycleHold", tailStart: 0.80 },
        { path: "event:/Robot/Recycle/Complete", file: "Recycling/G04_Recycle_Complete.wav", reserved: true }
    ];
    var workspace = studio.project.workspace;
    var preserved = [];
    var imported = {};

    function requireCondition(value, message) {
        if (!value) throw new Error("Milestone 3 authoring: " + message);
    }
    function exactType(object, type) { return object && object.isOfExactType(type); }
    function closeEnough(a, b) { return Math.abs(a - b) < 0.000001; }
    function leafName(path) { return path.substring(path.lastIndexOf("/") + 1); }
    function layersFor(definition) { return definition.layers || [definition]; }
    function sourcePath(definition, layer) { return (definition.sourceRoot || sourceRoot) + layer.file; }
    function parameterName(definition, kind) { return "M3_" + leafName(definition.path) + "_" + kind; }
    function normalizeParameter(parameter) {
        if (exactType(parameter, "ParameterProxy")) return parameter.preset;
        if (exactType(parameter, "ParameterPreset")) return parameter.parameter;
        return parameter;
    }
    function assetMatches(file) {
        return studio.project.model.AudioFile.findInstances().filter(function (asset) {
            return String(asset.getAssetPath()).replace(/\\/g, "/") === leafName(file);
        });
    }
    function existingLoops(event) {
        return studio.project.model.LoopRegion.findInstances().filter(function (region) {
            return region.timeline && region.timeline.id === event.timeline.id;
        });
    }
    function checkSimpleEvent(event, definition, bank) {
        requireCondition(event.isValid && !event.is3D(), "Invalid or non-2D event " + definition.path);
        requireCondition(event.banks.length === 0 ||
            (bank && event.banks.length === 1 && event.banks[0].id === bank.id),
            "Existing event belongs to another bank: " + definition.path);
        var layerCount = layersFor(definition).length;
        requireCondition(event.groupTracks.length <= layerCount, "Refusing to replace extra tracks in " + definition.path);
        requireCondition(event.timeline.modules.length <= layerCount && event.timeline.modules.every(function (module) {
            return exactType(module, "SingleSound");
        }), "Refusing to replace complex timeline content in " + definition.path);
        event.groupTracks.forEach(function (track) {
            requireCondition(event.timeline.modules.filter(function (module) {
                return module.audioTrack && module.audioTrack.id === track.id;
            }).length <= 1, "Refusing to replace multiple sounds on one track in " + definition.path);
        });
        var loops = existingLoops(event);
        requireCondition(loops.length <= (definition.loop ? 1 : 0), "Unexpected loop regions in " + definition.path);
        requireCondition(loops.every(function (loop) {
            var conditions = loop.triggerConditions;
            if (conditions.length > (definition.holdName ? 1 : 0)) return false;
            if (conditions.length && (!exactType(conditions[0], "ParameterCondition") ||
                !conditions[0].parameter.presetOwner ||
                conditions[0].parameter.presetOwner.name !== definition.holdName)) return false;
            var transition = loop.transitionTimeline;
            return !transition || (definition.crossfade > 0 && exactType(transition, "TransitionTimeline") &&
                transition.modules.length === 2 && transition.modules.filter(function (module) {
                    return exactType(module, "TransitionSourceSound");
                }).length === 1 && transition.modules.filter(function (module) {
                    return exactType(module, "TransitionDestinationSound");
                }).length === 1);
        }), "Refusing to replace custom loop conditions or transitions in " + definition.path);
        parameterKinds.forEach(function (kind) {
            var name = parameterName(definition, kind);
            var matches = event.getParameterPresets().map(normalizeParameter).filter(function (parameter) {
                return parameter.presetOwner && parameter.presetOwner.name === name;
            });
            requireCondition(matches.length <= 1 && matches.every(function (parameter) {
                return exactType(parameter, "GameParameter");
            }), "Invalid or duplicate parameter " + name);
        });
    }
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
    function deliveredAsset(definition, layer) {
        var path = sourcePath(definition, layer);
        if (imported[path]) return imported[path];
        var matches = assetMatches(layer.file);
        requireCondition(matches.length <= 1, "Duplicate root audio asset " + leafName(layer.file));
        // Existing assets retain their GUIDs. They must have been synchronized
        // before this authoring pass; replacing or processing WAVs is separate.
        var asset = matches.length ? matches[0] : studio.project.importAudioFile(path);
        requireCondition(asset && asset.isValid && asset.length > 0, "Import or lookup failed for " + layer.file);
        imported[path] = asset;
        return asset;
    }
    function sourceWindow(layer, asset) {
        var start = layer.trimStart === undefined ? 0 : layer.trimStart;
        var end = layer.trimEnd === undefined ? asset.length : layer.trimEnd;
        requireCondition(start >= 0 && end > start && end <= asset.length,
            "Requested source window exceeds " + layer.file);
        return { start: start, end: end, length: end - start };
    }
    function configureFade(sound, seconds, fadeIn) {
        requireCondition(seconds > 0 && seconds < sound.length, "Fade exceeds source window");
        var property = fadeIn ? "fadeInCurve" : "fadeOutCurve";
        var curve = sound[property];
        requireCondition(!curve || exactType(curve, "FadeCurve"), "Unexpected sound fade type");
        if (!curve) {
            curve = studio.project.create("FadeCurve");
            sound[property] = curve;
            curve.startPoint = studio.project.create("AutomationPoint");
            curve.endPoint = studio.project.create("AutomationPoint");
        }
        curve.startPoint.position = fadeIn ? sound.start : sound.start + sound.length - seconds;
        curve.startPoint.value = fadeIn ? 0 : 1;
        curve.startPoint.curveShape = 0;
        curve.endPoint.position = fadeIn ? sound.start + seconds : sound.start + sound.length;
        curve.endPoint.value = fadeIn ? 1 : 0;
        requireCondition(curve.isValid, "Invalid sound fade");
    }
    function configureParameters(event, definition) {
        return parameterKinds.map(function (kind) {
            var name = parameterName(definition, kind);
            var matches = event.getParameterPresets().map(normalizeParameter).filter(function (parameter) {
                return parameter.presetOwner && parameter.presetOwner.name === name;
            });
            requireCondition(matches.length <= 1, "Duplicate parameter " + name);
            var parameter = matches.length ? matches[0] : normalizeParameter(event.addGameParameter({
                name: name, type: studio.project.parameterType.User, min: 0, max: 1
            }));
            requireCondition(exactType(parameter, "GameParameter"), "Expected a game parameter for " + name);
            // FMOD preset names are project-wide unique although these parameter
            // values belong to each event instance. Runtime caches their IDs.
            parameter.presetOwner.name = name;
            parameter.minimum = 0;
            parameter.maximum = 1;
            parameter.initialValue = 0;
            parameter.isGlobal = false;
            parameter.isReadOnly = false;
            parameter.isHeld = false;
            requireCondition(parameter.isValid && parameter.presetOwner.name === name, "Invalid parameter " + name);
            return name;
        });
    }
    function configureHold(event, loop, name) {
        var matches = event.getParameterPresets().map(normalizeParameter).filter(function (parameter) {
            return parameter.presetOwner && parameter.presetOwner.name === name;
        });
        requireCondition(matches.length <= 1, "Duplicate hold parameter " + name);
        var hold = matches.length ? matches[0] : normalizeParameter(event.addGameParameter({
            name: name, type: studio.project.parameterType.User, min: 0, max: 1
        }));
        requireCondition(exactType(hold, "GameParameter"), "Hold must be a game parameter");
        hold.presetOwner.name = name;
        hold.minimum = 0;
        hold.maximum = 1;
        hold.initialValue = 1;
        hold.isGlobal = false;
        hold.isReadOnly = false;
        hold.isHeld = false;
        var conditions = loop.triggerConditions;
        requireCondition(conditions.length <= 1, "Unexpected extra loop conditions in " + event.getPath());
        var condition = conditions.length ? conditions[0] : loop.addParameterCondition(hold, 0.5, 1);
        requireCondition(exactType(condition, "ParameterCondition") && condition.parameter.id === hold.id,
            "Unexpected condition on " + event.getPath());
        condition.minimum = 0.5;
        condition.maximum = 1;
        requireCondition(hold.isValid && condition.isValid && hold.presetOwner.name === name, "Invalid hold condition");
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
    studio.project.model.Event.findInstances().forEach(function (event) {
        preserved.push({ path: event.getPath(), id: event.id });
    });
    var active = definitions.filter(function (definition) {
        var ready = true;
        layersFor(definition).forEach(function (layer) {
            var path = sourcePath(definition, layer);
            if (!studio.system.getFile(path).exists()) {
                console.log((definition.reserved ? "M3_RESERVED_NOT_DELIVERED " : "M3_NOT_DELIVERED ") + path);
                ready = false;
            }
        });
        return ready;
    });
    requireCondition(active.length > 0, "No delivered sources found (nothing changed)");
    var banks = studio.project.model.Bank.findInstances().filter(function (bank) { return bank.name === bankName; });
    requireCondition(banks.length <= 1, "Duplicate " + bankName + " banks");
    var bank = banks.length ? banks[0] : null;
    requireCondition(!bank || !bank.isMasterBank, bankName + " cannot replace the master bank");
    active.forEach(function (definition) {
        layersFor(definition).forEach(function (layer) {
            var matches = assetMatches(layer.file);
            requireCondition(matches.length <= 1 && matches.every(function (asset) {
                return asset.isValid && asset.length > 0;
            }), "Invalid or duplicate root asset for " + layer.file);
            if (matches.length) {
                var window = sourceWindow(layer, matches[0]);
                if (definition.loopEnd !== undefined)
                    requireCondition(definition.loopEnd + (definition.crossfade || 0) <= window.length,
                        "Loop and crossfade exceed source " + layer.file);
            }
        });
        var event = studio.project.lookup(definition.path);
        if (event) checkSimpleEvent(event, definition, bank);
    });

    // All no-delivery and conflicting-existing-event checks above are read-only.
    var robot = findFolder("Robot", workspace.masterEventFolder);
    var folders = { Balance: findFolder("Balance", robot), Recycle: findFolder("Recycle", robot) };
    if (!bank) {
        bank = studio.project.create("Bank");
        bank.name = bankName;
        bank.folder = workspace.masterBankFolder;
    }
    active.forEach(function (definition) {
        var event = studio.project.lookup(definition.path);
        var previousId = event ? event.id : null;
        if (!event) event = workspace.addEvent(leafName(definition.path), false);
        requireCondition(event && event.isValid, "Invalid event " + definition.path);
        event.folder = definition.path.indexOf("/Balance/") !== -1 ? folders.Balance : folders.Recycle;
        if (!event.banks.length) event.relationships.banks.add(bank);
        var layers = layersFor(definition);
        var authored = layers.map(function (layer, index) {
            var asset = deliveredAsset(definition, layer);
            var window = sourceWindow(layer, asset);
            var track = event.groupTracks.length > index ? event.groupTracks[index] : event.addGroupTrack(layer.track || "Audio");
            var sounds = event.timeline.modules.filter(function (module) {
                return module.audioTrack && module.audioTrack.id === track.id;
            });
            requireCondition(sounds.length <= 1, "Unexpected extra layer sounds in " + definition.path);
            var sound = sounds.length ? sounds[0] : track.addSound(event.timeline, "SingleSound", 0, window.length);
            sound.audioFile = asset;
            sound.start = 0;
            sound.length = window.length;
            sound.isAsync = false;
            sound.looping = false;
            sound.startOffset = 100 * window.start / asset.length;
            if (layer.fadeIn) configureFade(sound, layer.fadeIn, true);
            if (layer.fadeOut) configureFade(sound, layer.fadeOut, false);
            requireCondition(sound.isValid, "Invalid source sound in " + definition.path);
            return { sound: sound, sourceSeconds: asset.length, file: sourcePath(definition, layer), window: window };
        });
        var sound = authored[0].sound;
        if (definition.loop) {
            requireCondition(authored.length === 1, "Looping layered events are unsupported");
            var loopStart = definition.loopStart === undefined ? 0 : definition.loopStart;
            var loopEnd = definition.loopEnd === undefined ? sound.length : definition.loopEnd;
            var crossfade = definition.crossfade || 0;
            requireCondition(loopStart >= 0 && loopEnd > loopStart && loopEnd + crossfade <= sound.length,
                "Loop and crossfade exceed source " + definition.file);
            if (definition.tailStart !== undefined)
                requireCondition(loopEnd + crossfade < definition.tailStart && definition.tailStart < sound.length,
                    "Loop crosses the closing tail in " + definition.path);
            var loops = existingLoops(event);
            var markerTrack = event.markerTracks.length ? event.markerTracks[0] : event.addMarkerTrack();
            var loop = loops.length ? loops[0] : markerTrack.addRegion(loopStart, loopEnd - loopStart, "Loop",
                studio.project.regionLoopMode.Looping);
            loop.position = loopStart;
            loop.length = loopEnd - loopStart;
            if (definition.holdName) configureHold(event, loop, definition.holdName);
            if (crossfade > 0) configureCrossfade(loop, sound, crossfade);
            requireCondition(loop.isValid && closeEnough(loop.position + loop.length, loopEnd), "Invalid loop in " + definition.path);
        }
        var parameters = configureParameters(event, definition);
        requireCondition(sound.isValid && !event.is3D(), "Invalid or non-2D event " + definition.path);
        requireCondition(event.isOneShot() === !definition.loop, "Unexpected one-shot state in " + definition.path);
        requireCondition(!previousId || previousId === event.id, "Existing M3 event GUID changed");
        console.log("M3_EVENT " + JSON.stringify({ path: definition.path, guid: event.id,
            sources: authored.map(function (layer) { return { file: layer.file, sourceSeconds: layer.sourceSeconds,
                trimStart: layer.window.start, trimEnd: layer.window.end, soundSeconds: layer.sound.length }; }), loop: !!definition.loop,
            loopStart: definition.loopStart, loopEnd: definition.loopEnd, crossfade: definition.crossfade || 0,
            hold: definition.holdName || null, tailStart: definition.tailStart,
            parameters: parameters, reserved: !!definition.reserved }));
    });
    preserved.forEach(function (entry) {
        var event = studio.project.lookup(entry.path);
        requireCondition(event && event.id === entry.id, "Existing event GUID changed: " + entry.path);
    });
    requireCondition(studio.project.save(), "Saving the M3 source project failed");
    console.log("M3_READY " + active.length + " delivered 2D events in " + bankName +
        "; " + preserved.length + " existing event GUIDs retained. Build and bind separately.");
}());

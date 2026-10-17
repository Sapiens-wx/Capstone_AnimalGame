/*
 * Select terrain or biological scan audio in the existing Scan/Pulse event.
 * Back up the external Studio project before running this script. It preserves
 * every existing event GUID and the original terrain instrument/source. The
 * existing biological track is reused for the complete new Bio_Scan.wav.
 * First sync changed sources byte-for-byte into the flat Studio Assets and
 * refresh their metadata. Existing Bio_Scan.wav is reused, not reimported.
 * Never runs the Milestone 1 initializer. Build RobotTools separately.
 */
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var source = "C:/Users/PC/Capstone_AnimalGame/FMOD/SourceAudio/Milestone1/Scan/Bio_Scan.wav";
    var pulsePath = "event:/Robot/Scan/Pulse";
    var parameterName = "BioScanLayer";
    var trackName = "Biological Layer";

    function requireCondition(value, message) {
        if (!value) throw new Error("Biological scan layer: " + message);
    }
    function exactType(object, type) { return object && object.isOfExactType(type); }
    function biologicalAsset() {
        var matches = studio.project.model.AudioFile.findInstances().filter(function (asset) {
            return String(asset.getAssetPath()).replace(/\\/g, "/") === "Bio_Scan.wav";
        });
        requireCondition(matches.length <= 1, "Duplicate root Bio_Scan.wav assets");
        var asset = matches.length ? matches[0] : studio.project.importAudioFile(source);
        requireCondition(asset && asset.isValid && asset.length > 0, "Import or lookup failed for Bio_Scan.wav");
        return asset;
    }
    function normalizeParameter(parameter) {
        if (exactType(parameter, "ParameterProxy")) return parameter.preset;
        if (exactType(parameter, "ParameterPreset")) return parameter.parameter;
        return parameter;
    }
    function originalState(sound) {
        return JSON.stringify({ id: sound.id, asset: sound.audioFile.id, track: sound.audioTrack.id,
            start: sound.start, length: sound.length, startOffset: sound.startOffset,
            isAsync: sound.isAsync, isCutoff: sound.isCutoff, looping: sound.looping,
            pitch: sound.pitch });
    }
    function removeOldPulseGain(sound, parameter) {
        var automators = sound.automators.filter(function (automator) {
            return automator.nameOfPropertyBeingAutomated === "volume";
        });
        requireCondition(automators.length <= 1, "Duplicate original Pulse volume automation");
        if (!automators.length) return;
        var automator = automators[0];
        var curves = automator.automationCurves;
        requireCondition(curves.length <= 1, "Refusing to replace complex original Pulse volume automation");
        if (curves.length) {
            var curve = curves[0];
            requireCondition(curve.parameter && curve.parameter.id === parameter.id,
                "Original Pulse volume is automated by another parameter");
            requireCondition(curve.automationPoints.length <= 2,
                "Refusing to remove a complex original Pulse gain curve");
        }
        requireCondition(studio.project.deleteObject(automator), "Cannot remove the former biological Pulse gain curve");
        requireCondition(sound.automators.filter(function (item) {
            return item.nameOfPropertyBeingAutomated === "volume";
        }).length === 0, "Original Pulse gain automation remains");
    }
    function configureSelection(sound, parameter, value, name) {
        var conditions = sound.triggerConditions;
        requireCondition(conditions.length <= 1, "Unexpected " + name + " conditions");
        var condition = conditions.length ? conditions[0] : sound.addParameterCondition(parameter, value, value);
        requireCondition(exactType(condition, "ParameterCondition") && condition.parameter.id === parameter.id,
            name + " condition targets another parameter");
        condition.minimum = value;
        condition.maximum = value;
        requireCondition(condition.isValid, "Invalid " + name + " condition");
    }

    requireCondition(String(studio.project.filePath).replace(/\\/g, "/").toLowerCase() === expectedProject,
        "Refusing to modify a different Studio source project");
    requireCondition(studio.version.productVersion === 2 && studio.version.majorVersion === 3,
        "Requires the FMOD Studio 2.03 scripting API");
    requireCondition(studio.system.getFile(source).exists(), "Missing Bio_Scan.wav");

    var events = studio.project.model.Event.findInstances();
    var eventIds = events.map(function (event) { return { path: event.getPath(), id: event.id }; });
    var pulse = studio.project.lookup(pulsePath);
    requireCondition(pulse && pulse.isValid && !pulse.is3D() && pulse.isOneShot(), "Existing 2D one-shot Pulse is required");
    requireCondition(pulse.banks.length === 1 && pulse.banks[0].name === "RobotTools", "Pulse must retain RobotTools bank");
    var pulseId = pulse.id;
    var bankId = pulse.banks[0].id;
    var layerTracks = pulse.groupTracks.filter(function (track) { return track.mixerGroup.name === trackName; });
    requireCondition(layerTracks.length <= 1, "Duplicate biological layer tracks");
    var layerTrack = layerTracks.length ? layerTracks[0] : null;
    var originals = pulse.timeline.modules.filter(function (module) {
        return !layerTrack || !module.audioTrack || module.audioTrack.id !== layerTrack.id;
    });
    requireCondition(originals.length === 1 && exactType(originals[0], "SingleSound"), "Refusing to alter complex original Pulse content");
    var original = originals[0];
    requireCondition(original.audioFile && original.start === 0 && original.length > 0 && !original.looping,
        "Unexpected original Pulse instrument");
    var originalSnapshot = originalState(original);
    var layerSounds = layerTrack ? pulse.timeline.modules.filter(function (module) {
        return module.audioTrack && module.audioTrack.id === layerTrack.id;
    }) : [];
    requireCondition(layerSounds.length <= 1 && (!layerSounds.length || exactType(layerSounds[0], "SingleSound")),
        "Refusing to replace complex biological layer content");
    var parameters = pulse.getParameterPresets().map(normalizeParameter).filter(function (parameter) {
        return parameter.presetOwner && parameter.presetOwner.name === parameterName;
    });
    requireCondition(parameters.length <= 1, "Duplicate BioScanLayer parameters");

    var asset = biologicalAsset();
    requireCondition(original.audioFile.id !== asset.id, "Terrain and biological sources must remain separate");
    var parameter = parameters.length ? parameters[0] : normalizeParameter(pulse.addGameParameter({
        name: parameterName, type: studio.project.parameterType.User, min: 0, max: 1
    }));
    requireCondition(exactType(parameter, "GameParameter"), "BioScanLayer must be a game parameter");
    parameter.presetOwner.name = parameterName;
    parameter.minimum = 0;
    parameter.maximum = 1;
    parameter.initialValue = 0;
    parameter.isGlobal = false;
    parameter.isReadOnly = false;
    parameter.isHeld = false;
    removeOldPulseGain(original, parameter);
    original.volume = 0;
    configureSelection(original, parameter, 0, "Terrain Pulse");

    if (!layerTrack) layerTrack = pulse.addGroupTrack(trackName);
    var layer = layerSounds.length ? layerSounds[0] : layerTrack.addSound(pulse.timeline, "SingleSound", 0, original.length);
    layer.audioFile = asset;
    layer.start = 0;
    // A finite asynchronous instrument plays the complete original asset after
    // triggering. Its short trigger region preserves terrain's original
    // timeline length; terrain never triggers the layer or allocates its voice.
    layer.length = original.length;
    layer.startOffset = 0;
    layer.isAsync = true;
    layer.isCutoff = false;
    layer.looping = false;
    layer.volume = 0;
    layer.pitch = 0;
    configureSelection(layer, parameter, 1, "Biological scan");

    requireCondition(layer.isValid && parameter.isValid && pulse.isOneShot(), "Invalid biological scan configuration");
    requireCondition(pulse.id === pulseId && pulse.banks.length === 1 && pulse.banks[0].id === bankId, "Pulse GUID or bank changed");
    requireCondition(originalState(original) === originalSnapshot, "Original Pulse instrument changed");
    requireCondition(pulse.groupTracks.length === 2 && pulse.timeline.modules.length === 2, "Unexpected Pulse content count");
    requireCondition(studio.project.model.Event.findInstances().length === eventIds.length, "Event count changed");
    eventIds.forEach(function (saved) {
        requireCondition(studio.project.lookup(saved.path).id === saved.id, "Existing event GUID changed: " + saved.path);
    });
    requireCondition(studio.project.save(), "Saving the biological layer failed");
    console.log("BIO_SCAN_LAYER_READY " + JSON.stringify({ event: pulsePath, guid: pulse.id,
        originalSound: original.id, layerSound: layer.id, parameter: parameterName, defaultValue: 0,
        terrainCondition: [0, 0], biologicalCondition: [1, 1], source: "Bio_Scan.wav",
        layerSeconds: asset.length, triggerSeconds: layer.length,
        isAsync: layer.isAsync, isCutoff: layer.isCutoff, looping: layer.looping,
        terrainPulseGain: 1, biologicalPulseGain: 0, biologicalGain: 1,
        preservedEvents: eventIds.length }));
}());

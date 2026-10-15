/*
 * Add Anthony's biological scan layer to the existing Scan/Pulse event.
 * Back up the external Studio project before running this script. It preserves
 * every existing event GUID and the original Pulse instrument/source. Only
 * biological Pulse gain is automated; terrain retains its original level.
 * Never runs the Milestone 1 initializer. Build RobotTools separately.
 */
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var source = "C:/Users/PC/Capstone_AnimalGame/FMOD/SourceAudio/Milestone1/Scan/Bio_Scan_layer.wav";
    var pulsePath = "event:/Robot/Scan/Pulse";
    var parameterName = "BioScanLayer";
    var trackName = "Biological Layer";
    var biologicalPulseDb = 20 * Math.log(0.8) / Math.LN10;
    var biologicalLayerDb = 20 * Math.log(0.4) / Math.LN10;

    function requireCondition(value, message) {
        if (!value) throw new Error("Biological scan layer: " + message);
    }
    function exactType(object, type) { return object && object.isOfExactType(type); }
    function normalizeParameter(parameter) {
        if (exactType(parameter, "ParameterProxy")) return parameter.preset;
        if (exactType(parameter, "ParameterPreset")) return parameter.parameter;
        return parameter;
    }
    function originalState(sound) {
        return JSON.stringify({ id: sound.id, asset: sound.audioFile.id, track: sound.audioTrack.id,
            start: sound.start, length: sound.length, startOffset: sound.startOffset,
            isAsync: sound.isAsync, isCutoff: sound.isCutoff, looping: sound.looping,
            volume: sound.volume, pitch: sound.pitch,
            conditions: sound.triggerConditions.map(function (condition) { return condition.id; }) });
    }
    function configurePulseGain(sound, parameter) {
        var automators = sound.automators.filter(function (automator) {
            return automator.nameOfPropertyBeingAutomated === "volume";
        });
        requireCondition(automators.length <= 1, "Duplicate original Pulse volume automation");
        var automator = automators.length ? automators[0] : sound.addAutomator("volume");
        var curves = automator.automationCurves;
        requireCondition(curves.length <= 1, "Refusing to replace complex original Pulse volume automation");
        var curve = curves.length ? curves[0] : automator.addAutomationCurve(parameter);
        requireCondition(curve.parameter && curve.parameter.id === parameter.id,
            "Original Pulse volume is automated by another parameter");
        var points = curve.automationPoints.slice().sort(function (a, b) { return a.position - b.position; });
        requireCondition(points.length <= 2, "Refusing to replace a complex original Pulse gain curve");
        if (!points.length) points.push(curve.addAutomationPoint(0, 0));
        if (points.length < 2) points.push(curve.addAutomationPoint(1, biologicalPulseDb));
        points[0].position = 0;
        points[0].value = 0;
        points[0].curveShape = 0;
        points[1].position = 1;
        points[1].value = biologicalPulseDb;
        points[1].curveShape = 0;
        requireCondition(automator.isValid && curve.isValid, "Invalid original Pulse biological gain automation");
    }

    requireCondition(String(studio.project.filePath).replace(/\\/g, "/").toLowerCase() === expectedProject,
        "Refusing to modify a different Studio source project");
    requireCondition(studio.version.productVersion === 2 && studio.version.majorVersion === 3,
        "Requires the FMOD Studio 2.03 scripting API");
    requireCondition(studio.system.getFile(source).exists(), "Missing Bio_Scan_layer.wav");

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

    var asset = studio.project.importAudioFile(source);
    requireCondition(asset && asset.isValid && asset.length > 0, "Import failed for Bio_Scan_layer.wav");
    requireCondition(!layerSounds.length || (layerSounds[0].audioFile && layerSounds[0].audioFile.id === asset.id),
        "Existing biological track contains another source");
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
    configurePulseGain(original, parameter);

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
    layer.volume = biologicalLayerDb;
    layer.pitch = 0;
    var conditions = layer.triggerConditions;
    requireCondition(conditions.length <= 1, "Unexpected biological layer conditions");
    var condition = conditions.length ? conditions[0] : layer.addParameterCondition(parameter, 0.5, 1);
    requireCondition(exactType(condition, "ParameterCondition") && condition.parameter.id === parameter.id,
        "Biological layer condition targets another parameter");
    condition.minimum = 0.5;
    condition.maximum = 1;

    requireCondition(layer.isValid && condition.isValid && parameter.isValid && pulse.isOneShot(), "Invalid biological layer configuration");
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
        condition: [0.5, 1], layerSeconds: asset.length, triggerSeconds: layer.length,
        isAsync: layer.isAsync, isCutoff: layer.isCutoff, looping: layer.looping,
        terrainPulseGain: 1, biologicalPulseGain: 0.8, biologicalLayerGain: 0.4,
        biologicalPulseDb: biologicalPulseDb, biologicalLayerDb: layer.volume,
        preservedEvents: eventIds.length }));
}());

// Updates the existing event in place, preserving its GUID and source audio.
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var actualProject = String(studio.project.filePath).replace(/\\/g, "/").toLowerCase();
    if (actualProject !== expectedProject) {
        throw new Error("Scan loop adjustment refuses to modify a different authoring project.");
    }
    var events = studio.project.model.Event.findInstances();
    var ids = events.map(function (event) { return event.id; }).sort().join(",");
    var event = studio.project.lookup("event:/Robot/Scan/Charge");
    if (!event || !event.isValid) {
        throw new Error("Missing Scan/Charge event.");
    }
    var loops = studio.project.model.LoopRegion.findInstances().filter(function (region) {
        return region.timeline && region.timeline.id === event.timeline.id;
    });
    if (loops.length !== 1) {
        throw new Error("Expected exactly one existing scan sustain loop.");
    }
    var loopStart = 0.71;
    var loopEnd = 0.85;
    loops[0].position = loopStart;
    loops[0].length = loopEnd - loopStart;

    // A transition overlaps the outgoing tail with the next loop's first 10 ms.
    // The destination advances during the overlap, preserving the 140 ms period.
    var crossfadeSeconds = 0.01;
    var sounds = event.timeline.modules.filter(function (module) {
        return module.isOfExactType("SingleSound");
    });
    if (sounds.length !== 1 || sounds[0].isAsync || !sounds[0].audioFile
        || sounds[0].audioFile.length < loopEnd + crossfadeSeconds) {
        throw new Error("Expected the original synchronous charge instrument with its untrimmed tail.");
    }
    // Replacing the source can change its duration. Keep the existing instrument
    // and GUID, while matching its length to the newly imported complete file.
    sounds[0].length = sounds[0].audioFile.length;
    var transition = loops[0].transitionTimeline;
    if (!transition) {
        transition = studio.project.create("TransitionTimeline");
        transition.event = loops[0];
    }
    transition.length = crossfadeSeconds;

    function proxy(type) {
        var existing = transition.modules.filter(function (module) { return module.isOfExactType(type); });
        if (existing.length > 1) {
            throw new Error("Unexpected duplicate transition proxy: " + type);
        }
        var result = existing.length ? existing[0] : studio.project.create(type);
        result.audioTrack = sounds[0].audioTrack;
        result.parameter = transition;
        result.start = 0;
        result.length = crossfadeSeconds;
        result.isAsync = true;
        return result;
    }

    var source = proxy("TransitionSourceSound");
    var destination = proxy("TransitionDestinationSound");
    if (transition.modules.length !== 2) {
        throw new Error("Refusing to overwrite additional transition content.");
    }

    function fade(owner, related, fadeIn) {
        var type = fadeIn ? "TransitionDestinationFadeInCurve" : "TransitionSourceFadeOutCurve";
        var relation = fadeIn ? "fadeInCurve" : "fadeOutCurve";
        var curve = owner[relation];
        if (curve && !curve.isOfExactType(type)) {
            throw new Error("Unexpected existing fade type on " + owner.entity);
        }
        if (!curve) {
            curve = studio.project.create(type);
            owner[relation] = curve;
            curve.startPoint = studio.project.create("AutomationPoint");
            curve.endPoint = studio.project.create("AutomationPoint");
        }
        curve.relatedModule = related;
        curve.startPoint.position = 0;
        curve.startPoint.value = fadeIn ? 0 : 1;
        // The paired curved fades match FMOD's bundled transition examples.
        curve.startPoint.curveShape = fadeIn ? -0.2547189 : 0.25471893;
        curve.endPoint.position = crossfadeSeconds;
        curve.endPoint.value = fadeIn ? 1 : 0;
        if (!curve.isValid || !owner.isValid) {
            throw new Error("Invalid scan transition fade.");
        }
    }
    fade(source, destination, false);
    fade(destination, source, true);
    if (!transition.isValid || !loops[0].isValid) {
        throw new Error("Invalid scan transition timeline.");
    }
    if (!studio.project.save()) {
        throw new Error("Saving the updated scan loop failed.");
    }
    if (studio.project.model.Event.findInstances().map(function (item) { return item.id; }).sort().join(",") !== ids) {
        throw new Error("Event GUIDs changed unexpectedly.");
    }
    console.log("M1_SCAN_LOOP " + event.id + " start=" + loops[0].position + " end=" + (loops[0].position + loops[0].length) + " crossfade=" + crossfadeSeconds);
}());

# Procedural Segmented Body Generator For Godot 4.7 .NET

Build and pose segmented creatures in the Godot 4.7 editor using your own meshes. Define a torso, add limbs, and choose the segments that make up each limb. The tool measures the segment meshes, generates a skeleton and creates inverse kinematics (IK) targets so you can pose limbs by moving their endpoints.

To make the creature walk, assign a locomotion settings resource before generating. Grounded limbs will then take procedural steps according to the assigned settings as the body moves and turns.

# Asset Organisation

```
GodotProceduralSegmentedBodies
├── Materials
│   └── ProceduralSegmentedBodies
│       ├── Floor&WallsGreyGrid.tres
│       ├── RampGreyGrid.tres
│       └── UnevenCubesGreyGrid.tres
├── Meshes
│   └── ProceduralSegmentedBodies
│       ├── test_body.res
│       ├── test_foot.res
│       ├── test_hand.res
│       ├── test_head.res
│       └── test_segment.res
├── Resources
│   └── ProceduralSegmentedBodies
│       ├── BodyDefinitions
│       │   ├── TestBipedalBodyDefinition.tres
│       │   └── TestQuadrupedalBodyDefinition.tres
│       ├── LocomotionSettings
│       │   ├── DefaultBipedalLocomotionSettings.tres
│       │   └── DefaultQuadrupedalLocomotionSettings.tres
│       └── StepCurves
│           ├── DefaultStepLiftCurve.tres
│           └── DefaultStepProgressCurve.tres
├── Scenes
│   └── ProceduralSegmentedBodies
│       └── procedural_segmented_body_test_level.tscn
├── addons
│   └── SegmentedBodyGizmoPreview
│       └── plugin.cfg
└── project.godot
```

# Quick start: generate an example using test assets

Use a supplied body definition and locomotion settings to try the generator and see an example before building your own creature.

1. Open the project in Godot 4.7 .NET and build the C# project.
2. Open a 3D scene and add a `ProceduralSegmentedBody` node.
3. **OPTIONAL:** Enable the project's gizmo plugin by navigating to `Project>Project Settings...`, click on the `Plugins` tab and toggling the `Enabled` boolean for `Segmented Body Gizmo Preview`.
4. Select the node. In the inspector, drag a supplied example body definition from the FileSystem dock into the `Body Definition` field. If you enabled the `Segmented Body Gizmo Preview` plugin in the previous step, you can view a preview of the selected body definition by toggling the `Show Preview` boolean on the node.
5. Click `Generate / Regenerate From Definition`. The creature should appear in the viewport with its own meshes, skeleton, and IK targets.
> **Regenerating replaces all children and bones of the ProceduralSegmentedBody node.** Keep all cameras, player controllers, and other nodes you want to preserve outside of the ProceduralSegmentedBody hierarchy. To make lasting changes, edit the body, limb and segment definitions or locomotion settings rather than the generated meshes.
6. In the Scene Tree, select a limb's `TargetNode` marker and move it with the move tool. The limb should bend toward the marker. This works with or without locomotion settings assigned.

Test animated locomotion using the supplied test level.

1. **OPTIONAL:** To remap movement controls, navigate to `Project>Project Settings...` and click on the `Input Map` tab and assign the movement actions to your desired keys.
2. Open the `procedural_segmented_body_test_level` scene.
3. Select the existing `ProceduralSegmentedBody` node under `PlayerController` and generate a creature based on your desired `Body Definition` with an associated `Locomotion Settings` resource.
4. Press the `Run Current Scene (F6)` to run the scene and walk your creature around using the assigned keys - grounded limbs should step according to `Locomotion Settings` assigned in the selected `Body Definition`.

> Creature turning is controlled via mouse movement.

# Generate a body

## Create your own body definition

A `Body Definition` describes a single creature in a saveable, shareable resource `.res` file. 

To create a `Body Definition` first and begin creating a creature
- Select the `ProceduralSegmentedBody` node.
- Open the dropdown beside `Body Definition`, and choose `New BodyDef`. 
- Click on the newly created `BodyDef` to expand it and see its controls.
- Right click the resource and select `Save` to save and share this resource between scenes or creatures.

| Body Definition Control | Description |
| --- | --- |
| `Head Mesh` | **Optional.** Adds a head mesh on top of the torso, connected by a single bone. Leave empty to generate a headless creature. |
| `Torso Mesh` | **Required.** Supplies the central body mesh. Its bounds determine the default head and limb attachment positions. |
| `Joint Mesh` | **Optional.** Places this mesh at the generated segment joints. Leaving it empty omits joint visuals - bones and IK still work without it. |
| `Limbs` | Required (at least 1 limb). An Array that holds one `Limb Definition` per limb. The next section [Add limbs](#add-limbs) explains how to set limbs up. |
| `Inverse Kinematics Type` | Selects the solver used by the creature's limb chains: [CCDIK](https://docs.godotengine.org/en/stable/classes/class_ccdik3d.html#class-ccdik3d), [FABRIK](https://docs.godotengine.org/en/stable/classes/class_fabrik3d.html#fabrik3d), or [Jacobian IK](https://docs.godotengine.org/en/stable/classes/class_jacobianik3d.html#class-jacobianik3d). CCDIK and Jacobian IK seem to work a bit better than FABRIK with limbs which use joint limitations. |
| `Head Attachment Offset` | Moves the head from the top centre of the torso in torso-local coordinates. |
| `Head Rotation Degrees` | Rotates the head mesh relative to its attachment point, in degrees, |
| `Locomotion Settings` | **Optional.** Assign a `Locomotion Settings` resource to generate a locomotion controller for grounded limbs to animate creature movement with steps. Leave empty to generate a creature you can pose manually using its IK targets. |

## Add limbs

Expand `Limbs` in the `Body Definition`, add an array element, and create a new `LimbDef` in that slot. Give it a unique `Limb ID`, such as `FrontLeftLeg`, then choose a `Preset`.

`Preset` provides an easy, streamlined way to define the limb's starting attachment point, and determines whether its endpoint uses ground placement. 
> For example to create two legs, give each one the `Leg` preset, then use opposite X values (e.g.: 0.5 and -0.5) in `Attachment Position` to separate them. Use `Attachment Rotation Degrees` to angle the legs outward without moving their attachment points.

| Limb Definition Control | Description |
|---|---|
| `Limb ID` | **Required.** Unique name for this limb within the body. Used to name its generated bones, meshes and IK target. |
| `Segments` | Required (at least 1 segment). An Array that holds one `Segment Definition` per segment, ordered from the torso outward. The next section [Build segment chains](#build-segment-chains) explains how to build this chain. |
| `Attachment Position` | Moves the limb root relative to its attachment point, in torso-local coordinates. |
| `Attachment Rotation Degrees` | Rotates the whole limb around its attachment point. Use this to set the starting angle of a leg or arm. |
| `Preset` | `Leg` attaches below the torso and enables ground placement. `Arm` attaches to one side without ground placement. `Custom` lets you define the attachment location and grounding behaviour. |
| `Use Magnet Targets` | **Optional.** For limbs with >2 segments. Adds an intermediate IK target that influences how the limb bends as it reaches its end target. This is achieved by adding a second, preceding setting to this limb's IK solver. Adjust before final usage - incorrect placement can have undesirable results. After generation move the end target through its intended range. if the limb folds back on itself, adjust the magnet marker and test again. |
| `Arm Attach Side` | **Arm preset only.** Attaches to the left or right side of the torso. |
| `Step Group` | For grounded limbs only when step groups are enabled in `Locomotion Settings`. Limbs in the same numbered group can begin steps together. Use a value from 1 to Step Group Count (defined in Locomotion Settings). |
| `Custom Limb Direction` | **Custom preset only.** Sets the attachment location expressed as multiples of the torso's half-size on each axis. E.g.: (0, -1, 0) attaches at the bottom-center. |
| `Custom Ground Foot` | **Custom preset only.** Enables ground placement for the limb. If locomotion settings are assigned, the limb can also take procedural steps. |

**NOTE:** Limb Definition also includes End Mesh settings discussed below in [Set up feet or hands](#set-up-feet-or-hands)

## Build segment chains

Expand `Segments` in the `Limb Definition`. For each rigid piece of the limb (i.e. thigh, calf), add an array element, create a `SegmentDef` in that slot, and assign its `Mesh`. Order the segments from the torso outward. Assign feet and hands separately under [Set up feet or hands](#set-up-feet-or-hands).

Choose the `Length Axis` that runs along each mesh. The generator measures the mesh along that axis and uses the result to place the next joint. For example, in a two-segment leg limb, if a calf mesh is longest along its Y axis, choose `Y`. Increasing `Scale.Y` then lengthens both the visible calf mesh and the calf bone itself.

| Segment Definition Control | Description |
| --- | --- |
| `Mesh` | **Required.** The mesh used for this segment. Its bounds inform the segment length. |
| `Length Axis` | Local mesh axis used to measure length. The chain extends along the negative direction of the selected axis: left for X, down for Y, forward for Z. |
| `Rest Position Rotation Degrees` | Rotates the segment's bone relative to its parent. Segments farther along the chain follow this rotation. |
| `Mesh Offset` | Moves the visible mesh relative to the segment without moving either joint. |
| `Mesh Rotation Degrees` | Rotates the visible mesh around its centre without changing the bone direction or the selected length axis. |
| `Scale` | Scales the mesh on each axis. Scaling along `Length Axis` also changes the distance to the next joint - scaling perpendicular to `Length Axis` changes the segment's thickness. |
| `Joint Preset` | Sets how the joint at the start of this segment can rotate during IK. `Free` allows unrestricted rotation. `Knee` applies a preset X-axis bend restriction that I found simulated a knee joint reasonably well during testing (as seen in the `TestBipedalBodyDefinition` example body definition). `Custom` uses the settings below. |
| `Joint Rotation Axis` | **Custom preset only.** Selects the axis the joint can rotate around. Choose `All` to allow rotation around all axes. |
| `Limit Joint` | **Custom preset only.** Adds a cone limit on the joint's movement. If the preview plugin and option are enabled (*recommended for this option*), this restriction is displayed by a cone which signifies the allowed range of motion for this joint. Controlled by the next two settings. |
| `Joint Cone Angle Degrees` | **Custom Limit only.** Sets the opening of the allowed cone. A smaller angle restricts movement more, a larger angle restricts less. |
| `Joint Limit Rotation Degrees` | **Custom Limit only.** Aims the cone relative to the joint. Use this to change which directions are permitted without changing the segment's rest pose. |

> **Example:** For a thigh-calf chain, the thigh starts at the hip and the calf starts at the knee. Apply the `Knee` preset to the *calf segment*. If the ankle is in the correct place but the calf mesh looks sideways, adjust the `Mesh Rotation Degrees`. If the ankle itself needs to move in the rest pose, adjust `Rest Position Rotation Degrees`.

## Set up feet or hands

Return to the `Limb Definition` and assign an optional `End Mesh`. Use this for the foot, hand, or other rigid piece at the tip of the limb. It attaches to the end bone and is separate from segment entries in `Segments`.

To be clear, the following controls are found in `Limb Definition` under the `End Mesh Settings` group.

| End Mesh Control | Description |
| --- | --- |
| `End Mesh` | **Optional.** Mesh attached to the limb's end bone. Leave empty if the limb should end at its final joint. If unassigned, `End Mesh Rotation Degrees` and `End Mesh Offset` have no effect. |
| `End Mesh Rotation Degrees` | Rotates the mesh relative to the end bone. Use this first to point the toes or fingers in the intended direction. |
| `End Mesh Offset` | Moves the mesh relative to the end bone without moving the IK target. Use this to line up the top of a foot with the ankle or the base of a hand with the wrist. |
| `Ground Clearance` | **Grounded limbs only.** Adds a gap between the estimated sole and the ground. Use a small positive value if the foot should sit slightly above surfaces. |

> **Foot placement:** The IK target represents the ankle. The generator estimates the distance to the sole from the lowest point of the foot mesh's bounds after applying its rotation and offset. Position the foot so its sole sits below the ankle in the rest pose. During locomotion, the tool uses that distance to place the ankle above the surface hit.

# Procedural Animation

## Locomotion settings

To animate the creature's steps as it moves, assign a `LocomotionSettings` resource to `Locomotion Settings` in the `Body Definition`, then regenerate the body. Leave it unassigned if you only want to pose the creature or are defining your own movement animations using its IK targets.

Locomotion responds to the body's movement and rotation during runtime. Your player controller or enemy script moves the creature - and the locomotion controller decides when and where its grounded limbs should step and how. You can share one settings resource between creatures, or save separate resources to describe different gaits (e.g.: run, walk, hop).

### Stepping

| Locomotion Control | Description |
| --- | --- |
| `Animate Locomotion` | Allows grounded limbs to start new steps. Turning it off lets any step already in progress finish, but prevents any new steps from beginning. |
| `Step Trigger Distance` | How far a planted foot can lag behind its preferred stance along the movement direction before it becomes elgible to step. Smaller values produce more frequent steps. |
| `Step Ahead Distance` | How far ahead of the preferred stance to look for a landing, measured in direction of velocity. Larger values widen the gait. |
| `Backward Step Multiplier` | Scales the lookahead distance when moving backward. For example, `0.5` would use half of the defined lookahead distance when moving directly backward. |
| `Step Duration` | Time in seconds for a foot to move from its planted position to its landing position. |
| `Step Height` | Maximum lift added above the step path when `Step Lift Curve` reaches `1`. |
| `Turn Threshold Degrees` | Angle between the planted foot's orientation and its desired orientation that can trigger a turning step, even if the body is turning in-place. |
| `Step Progress Curve` | Controls how quickly the foot travels and rotates toward its landing pose during a step. **Use a curve that starts at `0` and ends at `1`.** |
| `Step Lift Curve` | Controls how high a foot lifts throughout a single step. **Use a curve that starts and ends at `0` so the foot is planted at both ends.** This value is a multiplier of `Step Height`, so a peak of `1` uses the full `Step Height`. |

> **Curve setup:** Both curves are required for procedural stepping and need at least two points each in order to be sampled by the locomotion script. The horizontal axis is fraction of the step completed: `0` is lift-off and `1` is landing. The vertical axis controls the travel progress or lift, respectively.

### Ground Detection

Each grounded limb uses two downward rays: one for checking the ground at its preferred stance, and the other for checking proposed landing positions ahead of it.

The following controls are under the `Ground Detection` group in a `LocomotionSettings` resource.

| Ground Detection Control | Description |
| --- | --- |
| `Ground Collision Mask` | Physics layers the rays can detect. Include the layer used for the floor or terrain collision and exclude the creature's own collider. |
| `Probe Height` | Distance above the preferred ankle position where each ground check starts. |
| `Probe Depth` | Distance below the preferred ankle position where each ground check ends. |
| `Maximum Slope Degrees` | Steepest surface accepted for valid foot placement, measured from a horizontal floor. |

### Step Groups

| Step Group Control | Description |
| --- | --- |
| `Use Step Groups` | When disabled, each grounded limb takes individual turns stepping. When enabled, eligible limbs in the same numbered group (assigned by `Step Group` in `LimbDef`) can begin a step together. |
| `Step Group Count` | Number of groups to cycle through, starting at `1`. Set each grounded limb's `Step Group` to a number up to (and including) this value in its `Limb Definition`. |

> **Quadruped example:** Set `Step Group Count` to `2`. Assign the front-left and back-right legs to group `1`. Assign the `front-right` and `back left` legs to group `2`. This will allow diagonally opposed pairs of legs to step together. An example of this can be found in the supplied `TestQuadrupedalBodyDefinition` resource.

## Adjust the generated stance

Each grounded limb has a preferred stance position. This is where the locomotion controller checks for ground relative to the body. Adjust the stance if, for example you want the creature to stand with its feet further apart without changing its limb lengths or rotations.

1. Generate the creature with `Locomotion Settings` assigned.
2. In the Scene tree, select a grounded limb's `TargetNode` marker. Use the Move tool to position it where you want the ankle to attempt to rest. (Move the IK target rather than the foot mesh itself).
3. Repeat for all other grounded limbs you want to reposition.
4. Select the generated `ProceduralLocomotion` node and click the **Set Current Target Positions as Rest Position** button. This records the current positions of *all grounded limb targets* relative to the body.
5. Save the scene and run it to test the new stance.

> **Example:** To widen a biped's stance, move the left and right ankle targets outward by equal amounts, then record their positions with the **Set Current Target Positions as Rest Position** button. To make the same creature take longer steps without changing stance width, adjust the `Step Ahead Distance` control instead.

The **Undo Target Position Changes** button swaps the recorded stance positions with the previous set. Use it after recording a stance you want to revert. It changes the stored positions used for locomotion. It does not move the markers back in-editor.

**Regenerating the creature rebuilds the targets and replaces the recorded stance. These edits are saved to the generated body, not its `Body Definition`.**

## Tune the walk

Start on flat ground and keep the creature moving at the same speed while comparing settings. Adjust one control at a time so you can see the effect it has. If multiple creatures share the same `LocomotionSettings` resource, duplicated it before tuning a gait for one creature or movement mode.

Once the gait looks right on flat ground, test it on a slope or uneven ground. If a foot does not find a landing, check the terrain's collision layer, probe reach, and `Maximum Slop Degrees` locomotion setting before changing step timing.

### Examples

| Desired Result | Adjustment |
| --- | --- |
| Let a foot stay planted longer before taking a step | Increase `Step Trigger Distance`. Decrease it if the body travels too far ahead of a planted foot. |
| Place the next step landing position farther ahead | Increase `Step Ahead Distance`. If the leg struggles to reach its landing, reduce it. |
| Make each foot complete its step faster | Decrease `Step Duration`. This changes how quickly the foot catches up with its preferred position, without changing the creature's movement speed. |
| Lift the feet higher | Increase `Step Height`. To change when the foot reaches its highest point throughout a step, move the peak of `Step Lift Curve` earlier or later as desired. |
| Make the feet follow the body's facing direction more closely | Decrease `Turn Threshold Degrees` so a smaller change in facing direction can trigger a turning step. |
| Shorten backward steps while retaining forward reach | Reduce `Backward Step Multiplier` and compare forward and backward movement at the same speed. |
| Encourage limbs with >2 segments arch upward | In the limb definition, enable `Use Magnet Targets`, regenerate, then adjust the generated target node - again, move the end target through its intended range. if the limb folds back on itself, adjust the magnet marker and test again. |

# Requirements

- **Godot:** Use the .NET edition of the editor. This project was built and tested with **Godot 4.7.2 .NET** (`v4.7.2.stable.mono.official [ed1daf0bf]`)
- **.NET SDK:** The project targets **.NET 8**.
- **Optional Preview:** Enable the included `Segmented Body Gizmo Preview` plugin to use the `Show Preview` option. The plugin is not required to generate or animate the creature - only to show a preview prior to generation.

To verify your setup, run this command from the project root:

```bash
dotnet build "PCG Segmented Bodies.csproj"
```
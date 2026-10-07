# Project to-do list

Work through these stages in order. Check off items when complete; keep unfinished polish visible.

## 1. Finish the character and animation prototype

- [x] Add pistols to the shared FPS/TPS weapon setup.
- [x] Add and tune pistol idle, aim, fire, reload, equip, and unequip animations.
- [ ] Make a second pass on hand grips, fingers, wrists, and elbow hints for every weapon.
- [ ] Polish FPS shoulder/arm offsets and consistency between weapons during swapping.
- [x] Refine the TPS equip/unequip animations.

## 2. Clean up and organize the project

- [ ] Remove unused assets, test prefabs, clips, and obsolete code after checking references.
- [ ] Refactor where it makes responsibilities clearer, without unnecessary script fragmentation.
- [ ] Group and move files for clearer separation between player, weapons, animation, gameplay, and imported assets.
- [ ] Keep the FPS_Animator source import isolated; put actively used game assets in Assets/Game.
- [ ] Simplify the prefab/configuration workflow for adding and tuning weapons.

## 3. Implement basic gameplay

- [ ] Define the minimum playable gameplay loop.
- [ ] Add shooting and hit detection.
- [ ] Add ammunition, reload rules, and weapon inventory/equipment logic.
- [ ] Add health, damage, death, and respawning.
- [ ] Add basic gameplay feedback and HUD.
- [ ] Verify that movement, actions, weapons, and animations work together in a local match.

## 4. Add a map

- [ ] Import a map suitable for the prototype.
- [ ] Set up collision, spawn points, and the basic match layout.
- [ ] Test the local gameplay loop on the map.

## 5. Build the application flow

Most of this stage can use stub implementations before real networking is added.

- [ ] Create the main menu scene.
- [ ] Create the lobby scene/flow.
- [ ] Create matchmaking screens and a stub matchmaking flow.
- [ ] Create a server browser and stub server listings/join flow.
- [ ] Connect menu, lobby, matchmaking/server browser, and gameplay scenes.
- [ ] Add basic connection, loading, failure, and return-to-menu UI states.

## 6. Create the working NGO baseline

- [ ] Clone the prototype into a separate NGO project.
- [ ] Implement real backend/networking using Netcode for GameObjects (NGO).
- [ ] Replace the application-flow stubs needed for an end-to-end networked experience.
- [ ] Network player spawning, movement/look, equipment, actions, damage, and respawning.
- [ ] Implement the required host/server, joining, lobby, scene transition, and disconnect flows.
- [ ] Test the complete product flow with multiple clients.
- [ ] Record the backend/networking features and responsibilities learned from this baseline.

## 7. Create the AtlasNet implementation

- [ ] Create another separate project clone for AtlasNet.
- [ ] Use the NGO baseline as a reference for expected behavior and backend/networking features.
- [ ] Implement the equivalent application and gameplay flows with AtlasNet.
- [ ] Identify gaps in the AtlasNet framework through the working game.
- [ ] Iterate on the framework to support those requirements.
- [ ] Test the complete end-to-end flow with multiple clients and compare against the NGO baseline.

The intent of this character controller is to recreate the feeling of driving a motorcycle but it's a human cc. 
Unlike traditional cc design, this cc uses and relies on forces instead of "setting velocity". A very physics-tied cc.

Players only spam left and right (no holding, must tap) to both:
 1. Move left and right (like a motorcycle)
 2. Adjust and "fix" the LEAN of the character. 
 
This cc has gameplay mechanic rules to follow:
 1. When the cc has a drunkness level at 0, the character stays completely up straight as they run and gain speed.
 2. Drunkenness level has no maximum. But at 10+ level Drunkenness, the character dangerously strongly rolls into the direction input by the player.
 3. The cc both yaws and rolls into the turn.
 4. At medium level Drunkenness, it is expected that the player, if turning right, will spam both "Left" and "Right" keys, but most the "Right" key as that is the direction they want to turn. But the Drunkenness is exaggerating the movements, risking the cc to roll fully into a 90 angle. 
 5. At roll degree X (positive or negative), the cc enters the "knockeddown" state due to having leaned over too much.
 6. knockeddown state == Rag-dolling for X amount of time, before being reset.
 7. Upon resetting, the cc starts again at 0 velocity and must gain momentum again.

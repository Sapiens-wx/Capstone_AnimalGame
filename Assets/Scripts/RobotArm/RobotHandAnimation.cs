using UnityEngine;

namespace AnimalGame.RobotArm
{
    // Animation integration points. Grabbing/recycling must work without animation assets.
    public class RobotHandAnimation : MonoBehaviour
    {
        public virtual void PlayGrab() { }
        public virtual void PlayRelease() { }
        public virtual void PlayRecycle() { }
    }
}

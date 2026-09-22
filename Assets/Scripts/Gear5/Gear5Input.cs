using UnityEngine.InputSystem;

namespace ToonGround
{
    /// <summary>
    /// Finds actions in the project-wide input actions (Assets/InputSystem_Actions),
    /// falling back to a code-built action so the prototype still works if the asset changes.
    /// </summary>
    static class Gear5Input
    {
        public static InputAction Find(string path, System.Func<InputAction> fallback, out bool owned)
        {
            var action = InputSystem.actions != null ? InputSystem.actions.FindAction(path) : null;
            owned = action == null;
            if (owned)
                action = fallback();
            action.Enable();
            return action;
        }

        public static void Release(InputAction action, bool owned)
        {
            if (action == null || !owned)
                return;
            action.Disable();
            action.Dispose();
        }

        public static InputAction Gear5Toggle()
        {
            var action = new InputAction("Gear5", InputActionType.Button);
            action.AddBinding("<Keyboard>/g");
            action.AddBinding("<Gamepad>/rightShoulder");
            return action;
        }

        public static InputAction Move()
        {
            var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            action.AddBinding("<Gamepad>/leftStick");
            return action;
        }

        public static InputAction Jump()
        {
            var action = new InputAction("Jump", InputActionType.Button);
            action.AddBinding("<Keyboard>/space");
            action.AddBinding("<Gamepad>/buttonSouth");
            return action;
        }
    }
}

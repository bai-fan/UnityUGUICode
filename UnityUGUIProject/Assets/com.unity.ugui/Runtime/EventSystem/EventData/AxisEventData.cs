namespace UnityEngine.EventSystems
{
    /// <summary>
    /// Event Data associated with Axis Events (Controller / Keyboard).
    /// 滚轮事件数据类
    /// 与Axis事件相关的数据(控制器/键盘)
    /// </summary>
    public class AxisEventData : BaseEventData
    {
        /// <summary>
        /// Raw input vector associated with this event.
        /// 移动的具体大小
        /// </summary>
        public Vector2 moveVector { get; set; }

        /// <summary>
        /// MoveDirection for this event.
        /// 移动的方向
        /// </summary>
        public MoveDirection moveDir { get; set; }

        public AxisEventData(EventSystem eventSystem)
            : base(eventSystem)
        {
            moveVector = Vector2.zero;
            moveDir = MoveDirection.None;
        }
    }
}

using System;

namespace FRFrame
{
    public abstract class FsmState
    {
        protected FsmController fsmController;

        public string StateName => GetType().Name;

        public virtual void OnInit(FsmController fsmController)
        {
            this.fsmController = fsmController;
        }

        public virtual void OnEnter()
        {
        }

        public virtual void OnExit()
        {
        }

        public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        public virtual void OnPause()
        {
        }

        public virtual void OnResume()
        {
        }

        protected bool ChangeState<T>() where T : FsmState
        {
            return fsmController.ChangeState<T>();
        }

        protected bool ChangeState(Type stateType)
        {
            return fsmController.ChangeState(stateType);
        }

        protected bool ChangeState(string stateName)
        {
            return fsmController.ChangeState(stateName);
        }

        protected void SetData(string key, object data)
        {
            fsmController.SetData(key, data);
        }

        protected object GetData(string key)
        {
            return fsmController.GetData(key);
        }

        protected T GetData<T>(string key)
        {
            return fsmController.GetData<T>(key);
        }

        protected bool TryGetData<T>(string key, out T value)
        {
            return fsmController.TryGetData(key, out value);
        }

        protected bool TryGetData<T>(out T value)
        {
            return fsmController.TryGetData(out value);
        }

        protected T GetDataOrDefault<T>(string key, T defaultValue = default)
        {
            return fsmController.GetDataOrDefault(key, defaultValue);
        }

        protected void RemoveData(string key)
        {
            fsmController.RemoveData(key);
        }
    }
}

using System;
using System.Linq;
using Avalonia.Data;

namespace Avalonia.Animation
{
    /// <summary>
    /// Provides statefulness for an iteration of a keyframe animation.
    /// </summary>
    internal class AnimatorStateMachine<T> : IObservable<object>, IDisposable
    {
        T _lastInterpValue;
        object _firstKFValue;

        private ulong _delayTotalFrameCount;
        private ulong _durationTotalFrameCount;
        private ulong _repeatCount;
        private ulong _currentIteration;
        private ulong _firstFrameCount;

        private bool _isLooping;
        private bool _isRepeating;
        private bool _isReversed;
        private bool _checkLoopAndRepeat;
        private bool _gotFirstKFValue;
        private bool _gotFirstFrameCount;

        private IAnimationTimer _timer;
        private FillMode _fillMode;
        private PlaybackDirection _animationDirection;
        private KeyFramesStates _currentState;
        private KeyFramesStates _savedState;
        private Animator<T> _parent;
        private Animation _targetAnimation;
        private Animatable _targetControl;
        private T _neutralValue;
        internal bool _unsubscribe = false;
        private IObserver<object> _targetObserver;

        [Flags]
        private enum KeyFramesStates
        {
            Initialize,
            DoDelay,
            DoRun,
            RunForwards,
            RunBackwards,
            RunApplyValue,
            RunComplete,
            Pause,
            Stop,
            Disposed
        }

        public void Initialize(
            IAnimationTimer timer,
            Animation animation,
            Animatable control,
            Animator<T> animator)
        {
            _timer = timer;
            _parent = animator;
            _targetAnimation = animation;
            _targetControl = control;
            _neutralValue = (T)_targetControl.GetValue(_parent.Property);

            //_delayTotalFrameCount = (ulong)(animation.Delay.Ticks / _timer.FrameTick.Ticks);
            //_durationTotalFrameCount = (ulong)(animation.Duration.Ticks / AnimationTimer.FrameTick.Ticks);

            switch (animation.RepeatCount.RepeatType)
            {
                case RepeatType.Loop:
                    _isLooping = true;
                    _checkLoopAndRepeat = true;
                    break;
                case RepeatType.Repeat:
                    _isRepeating = true;
                    _checkLoopAndRepeat = true;
                    _repeatCount = animation.RepeatCount.Value;
                    break;
            }

            _isReversed = (animation.PlaybackDirection & PlaybackDirection.Reverse) != 0;
            _animationDirection = _targetAnimation.PlaybackDirection;
            _fillMode = _targetAnimation.FillMode;

            if (_durationTotalFrameCount > 0)
                _currentState = KeyFramesStates.DoDelay;
            else
                _currentState = KeyFramesStates.DoRun;
        }

        public void Step(PlayState _playState, TimeSpan frameTick, Func<double, T, T> Interpolator)
        {
            try
            {
                InternalStep(_playState, (ulong)frameTick.Ticks, Interpolator);
            }
            catch (Exception e)
            {
                _targetObserver?.OnError(e);
            }
        }

        private void InternalStep(PlayState _playState, ulong frameTick, Func<double, T, T> Interpolator)
        {
            if (!_gotFirstKFValue)
            {
                _firstKFValue = _parent.First().Value;
                _gotFirstKFValue = true;
            }

            if (!_gotFirstFrameCount)
            {
                _firstFrameCount = frameTick;
                _gotFirstFrameCount = true;
            }

            if (_currentState == KeyFramesStates.Disposed)
                throw new InvalidProgramException("This KeyFrames Animation is already disposed.");

            if (_playState == PlayState.Stop)
                _currentState = KeyFramesStates.Stop;

            // Save state and pause the machine
            if (_playState == PlayState.Pause && _currentState != KeyFramesStates.Pause)
            {
                _savedState = _currentState;
                _currentState = KeyFramesStates.Pause;
            }

            // Resume the previous state
            if (_playState != PlayState.Pause && _currentState == KeyFramesStates.Pause)
                _currentState = _savedState;

            double tempDuration = 0d, easedTime;

            bool handled = false;

            while (!handled)
            {
                ulong delayFrameCount = frameTick - _firstFrameCount;
                ulong durationFrameCount = frameTick - _firstFrameCount - _delayTotalFrameCount;

                switch (_currentState)
                {
                    case KeyFramesStates.DoDelay:

                        if (_fillMode == FillMode.Backward
                         || _fillMode == FillMode.Both)
                        {
                            if (_currentIteration == 0)
                            {
                                _targetObserver.OnNext(_firstKFValue);
                            }
                            else
                            {
                                _targetObserver.OnNext(_lastInterpValue);
                            }
                        }

                        if (delayFrameCount > _delayTotalFrameCount)
                        {
                            _currentState = KeyFramesStates.DoRun;
                        }
                        else
                        {
                            handled = true;
                        }
                        break;

                    case KeyFramesStates.DoRun:

                        if (_isReversed)
                            _currentState = KeyFramesStates.RunBackwards;
                        else
                            _currentState = KeyFramesStates.RunForwards;

                        break;

                    case KeyFramesStates.RunForwards:

                        if (durationFrameCount > _durationTotalFrameCount)
                        {
                            _currentState = KeyFramesStates.RunComplete;
                        }
                        else
                        {
                            tempDuration = (double)durationFrameCount / _durationTotalFrameCount;
                            _currentState = KeyFramesStates.RunApplyValue;

                        }
                        break;

                    case KeyFramesStates.RunBackwards:

                        if (durationFrameCount > _durationTotalFrameCount)
                        {
                            _currentState = KeyFramesStates.RunComplete;
                        }
                        else
                        {
                            tempDuration = (double)(_durationTotalFrameCount - durationFrameCount) / _durationTotalFrameCount;
                            _currentState = KeyFramesStates.RunApplyValue;
                        }
                        break;

                    case KeyFramesStates.RunApplyValue:

                        easedTime = _targetAnimation.Easing.Ease(tempDuration);
                        _lastInterpValue = Interpolator(easedTime, _neutralValue);
                        _targetObserver.OnNext(_lastInterpValue);
                        _currentState = KeyFramesStates.DoRun;
                        handled = true;
                        break;

                    case KeyFramesStates.RunComplete:

                        if (_checkLoopAndRepeat)
                        {
                            _firstFrameCount = frameTick;

                            if (_isLooping)
                            {
                                _currentState = KeyFramesStates.DoRun;
                            }
                            else if (_isRepeating)
                            {
                                if (_currentIteration >= _repeatCount)
                                {
                                    _currentState = KeyFramesStates.Stop;
                                }
                                else
                                {
                                    _currentState = KeyFramesStates.DoRun;
                                }
                                _currentIteration++;
                            }

                            if (_animationDirection == PlaybackDirection.Alternate
                             || _animationDirection == PlaybackDirection.AlternateReverse)
                                _isReversed = !_isReversed;

                            break;
                        }

                        _currentState = KeyFramesStates.Stop;
                        break;

                    case KeyFramesStates.Stop:

                        if (_fillMode == FillMode.Forward
                         || _fillMode == FillMode.Both)
                        {
                            _targetControl.SetValue(_parent.Property, _lastInterpValue, BindingPriority.LocalValue);
                        }
                        _targetObserver.OnCompleted();
                        handled = true;
                        break;
                    default:
                        handled = true;
                        break;
                }
            }
        }

        public IDisposable Subscribe(IObserver<object> observer)
        {
            _targetObserver = observer;
            return this;
        }

        public void Dispose()
        {
            _unsubscribe = true;
            _currentState = KeyFramesStates.Disposed;
        }
    }
}

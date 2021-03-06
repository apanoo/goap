﻿using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GoapAgent : MonoBehaviour, IAgent
{
    private HashSet<GoapAction> availableActions;
    private Queue<GoapAction> currentActions;

    private IGoap dataProvider;
        // this is the implementing class that provides our world data and listens to feedback on planning

    private FSM.FSMState idleState; // finds something to do
    private FSM.FSMState moveToState; // moves to a target
    private FSM.FSMState performActionState; // performs an action
    private GoapPlanner planner;
    private FSM stateMachine;

    public void AbortFsm()
    {
        stateMachine.ClearState();
        stateMachine.pushState(idleState);
    }

    public void AddAction(GoapAction a)
    {
        availableActions.Add(a);
    }

    public GoapAction GetAction(Type action)
    {
        foreach (var g in availableActions)
        {
            if (g.GetType().Equals(action))
                return g;
        }
        return null;
    }

    public void RemoveAction(GoapAction action)
    {
        availableActions.Remove(action);
    }

    private void Start()
    {
        stateMachine = new FSM();
        availableActions = new HashSet<GoapAction>();
        currentActions = new Queue<GoapAction>();
        planner = new GoapPlanner();
        findDataProvider();
        createIdleState();
        createMoveToState();
        createPerformActionState();
        stateMachine.pushState(idleState);
        loadActions();
    }

    private void Update()
    {
        stateMachine.Update(gameObject);
    }

    private bool hasActionPlan()
    {
        return currentActions.Count > 0;
    }

    private void createIdleState()
    {
        idleState = (fsm, gameObj) =>
        {
            // GOAP planning

            // get the world state and the goal we want to plan for
            var worldState = dataProvider.getWorldState();
            var goals = dataProvider.createGoalState();

            // search enable Plan
            Queue<GoapAction> plan = null;
            KeyValuePair<string, bool> lastGoal = new KeyValuePair<string, bool>();
            foreach (var goal in goals)
            {
                lastGoal = goal;
                plan = planner.plan(gameObject, availableActions, worldState, goal,dataProvider);
                if (plan != null)
                    break;
            }
            if (plan != null)
            {
                // we have a plan, hooray!
                currentActions = plan;
                dataProvider.planFound(lastGoal, plan);

                fsm.popState(); // move to PerformAction state
                fsm.pushState(performActionState);
            }
            else
            {
                // ugh, we couldn't get a plan
                Debug.Log("<color=orange>Failed Plan:</color>" + prettyPrint(goals));
                dataProvider.planFailed(goals);
                fsm.popState(); // move back to IdleAction state
                fsm.pushState(idleState);
            }
        };
    }

    private void createMoveToState()
    {
        moveToState = (fsm, gameObj) =>
        {
            // move the game object

            var action = currentActions.Peek();
            if (action.requiresInRange() && action.target == null)
            {
                Debug.Log(
                    "<color=red>Fatal error:</color> Action requires a target but has none. Planning failed. You did not assign the target in your Action.checkProceduralPrecondition()");
                fsm.popState(); // move
                fsm.popState(); // perform
                fsm.pushState(idleState);
                return;
            }

            // get the agent to move itself
            if (dataProvider.moveAgent(action))
            {
                fsm.popState();
            }

            /*MovableComponent movable = (MovableComponent) gameObj.GetComponent(typeof(MovableComponent));
			if (movable == null) {
				Debug.Log("<color=red>Fatal error:</color> Trying to move an Agent that doesn't have a MovableComponent. Please give it one.");
				fsm.popState(); // move
				fsm.popState(); // perform
				fsm.pushState(idleState);
				return;
			}

			float step = movable.moveSpeed * Time.deltaTime;
			gameObj.transform.position = Vector3.MoveTowards(gameObj.transform.position, action.target.transform.position, step);

			if (gameObj.transform.position.Equals(action.target.transform.position) ) {
				// we are at the target location, we are done
				action.setInRange(true);
				fsm.popState();
			}*/
        };
    }

    private void createPerformActionState()
    {
        performActionState = (fsm, gameObj) =>
        {
            // perform the action

            if (!hasActionPlan())
            {
                // no actions to perform
                Debug.Log("<color=red>Done actions</color>");
                fsm.popState();
                fsm.pushState(idleState);
                dataProvider.actionsFinished();
                return;
            }

            var action = currentActions.Peek();
            if (action.isDone())
            {
                // the action is done. Remove it so we can perform the next one
                currentActions.Dequeue();
            }

            if (hasActionPlan())
            {
                // perform the next action
                action = currentActions.Peek();
                var inRange = action.requiresInRange() ? action.isInRange() : true;

                if (inRange)
                {
                    // we are in range, so perform the action
                    var success = action.perform(gameObj,dataProvider.GetBlackBoard());

                    if (!success)
                    {
                        // action failed, we need to plan again
                        fsm.popState();
                        fsm.pushState(idleState);
                        dataProvider.planAborted(action);
                    }
                }
                else
                {
                    // we need to move there first
                    // push moveTo state
                    fsm.pushState(moveToState);
                }
            }
            else
            {
                // no actions left, move to Plan state
                fsm.popState();
                fsm.pushState(idleState);
                dataProvider.actionsFinished();
            }
        };
    }

    private void findDataProvider()
    {
        foreach (var comp in gameObject.GetComponents(typeof (Component)))
        {
            if (typeof (IGoap).IsAssignableFrom(comp.GetType()))
            {
                dataProvider = (IGoap) comp;
                dataProvider.Agent = this;
                return;
            }
        }
    }

    private void loadActions()
    {
        var actions = gameObject.GetComponents<GoapAction>();
        foreach (var a in actions)
        {
            availableActions.Add(a);
        }
//        Debug.Log("Found actions: " + prettyPrint(actions));
    }

    public static string prettyPrint(HashSet<KeyValuePair<string, object>> state)
    {
        var s = "";
        foreach (var kvp in state)
        {
            s += kvp.Key + ":" + kvp.Value;
            s += ", ";
        }
        return s;
    }

    public static string prettyPrint(Queue<GoapAction> actions)
    {
        var s = "";
        foreach (var a in actions)
        {
            s += a.GetType().Name;
            s += "-> ";
        }
        s += "GOAL";
        return s;
    }
    public static string prettyPrint(Dictionary<string, bool> goals)
    {
        var s = "";
        foreach (var a in goals)
        {
            s += a.Key;
            s += "-> ";
        }
        s += "GOAL";
        return s;
    }

    public static string prettyPrint(GoapAction[] actions)
    {
        var s = "";
        foreach (var a in actions)
        {
            s += a.GetType().Name;
            s += ", ";
        }
        return s;
    }

    public static string prettyPrint(GoapAction action)
    {
        var s = "" + action.GetType().Name;
        return s;
    }
}
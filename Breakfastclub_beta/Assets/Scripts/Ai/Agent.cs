﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using System.Text;

public struct InteractionRequest
{
    public Agent source;
    public AgentBehavior action;

    public InteractionRequest(Agent source, AgentBehavior action)
    {
        this.source = source;
        this.action = action;
    }
}

public class Agent : MonoBehaviour
{
    // A started action will get a bias in order to be repeated during the next turns
    private readonly int STICKY_ACTION_SCORE = 50;
    private readonly int STICKY_ACTION_BIAS = 10;
    private int ticksOnThisTask;

    private readonly float HAPPINESS_INCREASE = 0.05f;

    [SerializeField] public int seed;

    private GlobalRefs GR;
    private CSVLogger Logger;

    [HideInInspector] public Classroom classroom;
    [HideInInspector] public NavMeshAgent navagent;
    [HideInInspector] public double turnCnt = 0;

    public Personality personality { get; protected set; }

    public float happiness { get; set; }
    public float energy { get; set; }
    public float attention { get; protected set;}

    //private List<AgentBehavior> behaviors = new List<AgentBehavior>();
    private Dictionary<string, AgentBehavior> behaviors = new Dictionary<string, AgentBehavior>();
    public AgentBehavior currentAction { get; protected set; }
    public AgentBehavior Desire { get; protected set; }

    private Queue pendingInteractions = new Queue();

    public System.Random random;


    // Start is called before the first frame update
    private void OnEnable()
    {
        random = new System.Random(seed);

        // Create a personality for this agent
        personality = new Personality(random);

        navagent = GetComponent<NavMeshAgent>();

        // Define all possible actions
        behaviors.Add("Break", new Break(this));
        behaviors.Add("Quarrel", new Quarrel(this));
        behaviors.Add("Chat", new Chat(this));
        behaviors.Add("StudyAlone", new StudyAlone(this));
        behaviors.Add("StudyGroup", new StudyGroup(this));

        // Set the default action state to Break
        currentAction = behaviors["Break"];
        Desire = behaviors["Break"];

        // Initiate Happiness and Energy
        energy = Math.Max(0.5f, random.Next(100)/100.0f); // with a value between [0.5, 1.0]
        happiness = Math.Max(-0.5f, 0.5f - random.Next(100)/100.0f); // with a value between [-0.5, 0.5]

        personality.extraversion = 0.9f;
    }

    void Start()
    {
        GR = GlobalRefs.Instance;
        Logger = GR.logger;
        classroom = GR.classroom;

        logInfo("Agent Personality: " + personality);

        //Agents AG = GameObject.Find("Agents").GetComponent<Agents>();
        logState();
    }

    public void interact(Agent source, AgentBehavior action)
    {
        pendingInteractions.Enqueue(new InteractionRequest(source, action));
    }

    // Log message as info
    public void logError(string message)
    {
        logX(message, "E");
    }

    public void logInfo(string message)
    {
        logX(message, "I");
    }

    public void logDebug(string message)
    {
        logX(message, "D");
    }

    public void logX(string message, string type)
    {
        string[] msg = { gameObject.name, turnCnt.ToString(), type, message };
        Logger.log(msg);
    }

    // Helper function logging Agent state
    private void logState()
    {
        logInfo(String.Format("Energy {0} Happiness {1} Attenion {2} | Action {3} Desire {4}", energy, happiness, attention, currentAction, Desire));
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        turnCnt++;

        updateAttention();

        logState();

        evaluate_current_action();

        handle_interactions();

        updateHappiness();
    }

    // attention = f(State, Environment, Personality)
    private void updateAttention()
    {
        attention = Math.Max((1.0f - classroom.noise) * personality.conscientousness * energy, 0.0f);
    }

    // If current_action equals desire we are happy, sad otherwise
    private void updateHappiness()
    {
        float change;
        if(currentAction == Desire)
        {
            change = HAPPINESS_INCREASE;
        }
        else
        {
            change = -HAPPINESS_INCREASE;
        }
        happiness = Math.Max(-1.0f, Math.Min(happiness + change, 1.0f));
    }


    private bool startAction(AgentBehavior newAction, bool setDesire=true, bool applyDefaultAction=true)
    {
        if (setDesire) {
            Desire = newAction;
        }
        if (newAction.possible())
        {
            if (newAction != currentAction)
            {
                logDebug(String.Format("Ending current action {0}.", currentAction.name));
                currentAction.end();

                logInfo(String.Format("Starting new action {0}. Executing ...", newAction.name));
                bool success = newAction.execute();
                if (!success)
                    logDebug(String.Format("Executing new action failed! Will continou anyways! ..."));
                currentAction = newAction;
                ticksOnThisTask = 0;
            }
            else
            {
                newAction.execute();
                ticksOnThisTask++;
            }
            return true;
        }
        else
        {
            if (applyDefaultAction)
            {
                // Agent cannot perform Action, go into Wait instead
                logInfo(String.Format("{0} is not possible. Executing break instead! ...", newAction));
                currentAction = behaviors["Break"];
                currentAction.execute();
            }
            return false;
        }
    }

    private void handle_interactions()
    {
        while(pendingInteractions.Count > 0)
        {
            InteractionRequest iR = (InteractionRequest)pendingInteractions.Dequeue();
            logInfo(String.Format("Interaction Request from {0} for action {1}", iR.source, iR.action));
            if (iR.action is Chat)
            {
                handle_Chat(iR.source);
            }
            else if (iR.action is Quarrel)
            {
                handle_Quarrel(iR.source);
            }
        }
    }

    private void handle_Chat(Agent otherAgent)
    {
        if( (currentAction is Chat) || (Desire is Chat))
        {
            logDebug(String.Format("Accept invitation to chat with {0} ...", otherAgent));
            Chat chat = (Chat)behaviors["Chat"];
            chat.acceptInviation(otherAgent);
            startAction(chat);
        }
        else
        {
            // An agent is convinced to chat based on its conscientousness trait.
            // Agents high on consciousness are more difficult to convince/distract
            float x = random.Next(100) / 100.0f;
            logDebug(String.Format("Agent proposal {0} >= {1} ...", x, personality.conscientousness));
            if (x >= personality.conscientousness)
            {
                logDebug(String.Format("Agent got convinced by {0} to start chatting ...", otherAgent));
                Chat chat = (Chat)behaviors["Chat"];
                chat.acceptInviation(otherAgent);
                startAction(chat, false, false);
            }
            else
            {
                logDebug(String.Format("Agent keeps to current action ({0} < {1})", x, personality.conscientousness));
            }
        }
    }

    private void handle_Quarrel(Agent otherAgent)
    {
        if( (currentAction is Quarrel) || (Desire is Quarrel))
        {
            logDebug(String.Format("Agent wanted to Quarrel! Now he can do so with {0} ...", otherAgent));
            Quarrel quarrel = (Quarrel)behaviors["Quarrel"];
            quarrel.acceptInviation(otherAgent);
            startAction(quarrel);
        }
        else
        {
            // An agent is convinced to chat based on its conscientousness trait.
            // Agents high on consciousness are more difficult to convince/distract
            float x = random.Next(100) / 100.0f;
            logDebug(String.Format("Agent proposal {0} >= {1} ...", x, personality.agreeableness));
            if (x >= personality.agreeableness)
            {
                logDebug(String.Format("Agent got convinced by {0} to start quarreling ...", otherAgent));
                Quarrel quarrel = (Quarrel)behaviors["Quarrel"];
                quarrel.acceptInviation(otherAgent);
                startAction(quarrel, false, false);
            }
            else
            {
                logDebug(String.Format("Agent keeps to current action ({0} < {1})", x, personality.agreeableness));
            }
        }
    }

    // Main Logic
    private void evaluate_current_action() 
    {
        StringBuilder sb = new StringBuilder();

        int best_rating = -1000;
        int rating = best_rating;
        AgentBehavior best_action = null;
        AgentBehavior behavior = null;

        foreach (KeyValuePair<string, AgentBehavior> kvp in behaviors)
        {
            behavior = kvp.Value;
            rating = behavior.rate();

            // The current action gets a score boost that declines exponetially
            if (behavior == currentAction)
            {
                // Agents high on consciousness will stick longer to chosen actions
                float lambda = 1.0f - personality.conscientousness;
                int score_bias = STICKY_ACTION_BIAS + (int)(STICKY_ACTION_SCORE * Math.Exp(-lambda * (float)ticksOnThisTask));
                rating += score_bias;
            }

            //logInfo(String.Format("Behavior: {0} rating {1}", behavior.name, rating));
            sb.Append(String.Format("{0}:{1} ", behavior.name, rating));
            if (rating > best_rating)
            {
                best_rating = rating;
                best_action = behavior;
            }
        }
        logInfo("Behavior: " + sb.ToString());

        if (best_action != null)
        {
            bool success = startAction(best_action);
            if(success)
                logInfo(String.Format("Starting Action {0}.", best_action));
            else
                logInfo(String.Format("Starting Action {0} failed!", best_action));
        }

    }
}

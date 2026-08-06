# What did I learn playing with this code?

## Planning
- There's no such thing as a simple distributed system.
- Creating a solid spec is hard. I probably started with too much scope. 
- Because the scope has high, the plan felt like an endless review loop. Decided it was “done” after 8 agent reviews. Probably took 2-3 hours.

## What worked well?
- Keeping track of progress using `tasks` folder.
- Implement loop triggered by `new-feature-with-agent-skills` command. Each task used followed build → review → pr → ship. This meant I was only reviewing code that had been completed with tests and had been through a round of agent review.  

## What didn't work so well?
- Some PRs slightly larger than I’d like making it harder to review.
- Managing multiple tasks isn’t easy. You need to make sure you are tracking everything running, each task isn't treading on each other's toes and you remember the context.
- Claude generates code faster than I can understand it. It's much harder to understand code that you haven't written. 

﻿/*
 *  Licensed to SharpSoftware under one or more contributor
 *  license agreements. See the NOTICE file distributed with this work for 
 *  additional information regarding copyright ownership.
 * 
 *  SharpSoftware licenses this file to you under the Apache License, 
 *  Version 2.0 (the "License"); you may not use this file except in 
 *  compliance with the License. You may obtain a copy of the License at
 * 
 *       http://www.apache.org/licenses/LICENSE-2.0
 * 
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 */

using Itinero.Optimization.Algorithms.Random;
using Itinero.Optimization.Algorithms.Solvers.GA;
using Itinero.Optimization.Logging;
using Itinero.Optimization.Tours;
using Itinero.Optimization.Tours.Cycles;
using System;
using System.Collections.Generic;

namespace Itinero.Optimization.TSP.Solvers.Operators
{
    /// <summary>
    /// An edge assembly crossover.
    /// </summary>
    public sealed class EAXOperator : ICrossOverOperator<float, TSProblem, TSPObjective, Tour, float>
    {
        private readonly int _maxOffspring;
        private readonly EdgeAssemblyCrossoverSelectionStrategyEnum _strategy;
        private readonly bool _nn;
        private readonly RandomGenerator _random;

        /// <summary>
        /// Creates a new EAX crossover.
        /// </summary>
        public EAXOperator()
            : this(30, EAXOperator.EdgeAssemblyCrossoverSelectionStrategyEnum.SingleRandom, true)
        {

        }

        /// <summary>
        /// Creates a new EAX crossover.
        /// </summary>
        public EAXOperator(int maxOffspring,
            EdgeAssemblyCrossoverSelectionStrategyEnum strategy,
            bool nn)
        {
            _maxOffspring = maxOffspring;
            _strategy = strategy;
            _nn = nn;

            _random = RandomGeneratorExtensions.GetRandom();
        }

        /// <summary>
        /// Returns the name of this operator.
        /// </summary>
        public string Name
        {
            get
            {
                if (_strategy == EdgeAssemblyCrossoverSelectionStrategyEnum.SingleRandom)
                {
                    if (_nn)
                    {
                        return string.Format("EAX_(SR{0}_NN)", _maxOffspring);
                    }
                    return string.Format("EAX_(SR{0})", _maxOffspring);
                }
                else
                {
                    if (_nn)
                    {
                        return string.Format("EAX_(MR{0}_NN)", _maxOffspring);
                    }
                    return string.Format("EAX_(MR{0})", _maxOffspring);
                }
            }
        }

        /// <summary>
        /// An enumeration of the crossover types.
        /// </summary>
        public enum EdgeAssemblyCrossoverSelectionStrategyEnum
        {
            /// <summary>
            /// SingleRandom.
            /// </summary>
            SingleRandom, // EAX-1AB
            /// <summary>
            /// MultipleRandom.
            /// </summary>
            MultipleRandom
        }

        #region ICrossOverOperation<int,Problem> Members

        private List<int> SelectCycles(
            List<int> cycles)
        {
            List<int> starts = new List<int>();
            if (_strategy == EdgeAssemblyCrossoverSelectionStrategyEnum.MultipleRandom)
            {
                foreach (int cycle in cycles)
                {
                    if (_random.Generate(1.0f) > 0.25)
                    {
                        starts.Add(cycle);
                    }
                }
                return starts;
            }
            else
            {
                if (cycles.Count > 0)
                {
                    int idx = _random.Generate(cycles.Count);
                    starts.Add(cycles[idx]);
                    cycles.RemoveAt(idx);
                }
            }
            return starts;
        }

        #endregion

        /// <summary>
        /// Applies this operator using the given solutions and produces a new solution.
        /// </summary>
        /// <returns></returns>
        public Tour Apply(TSProblem problem, TSPObjective objective, Tour solution1, Tour solution2, out float fitness)
        {
            if (solution1.Last != problem.Last) { throw new ArgumentException("Route and problem have to have the same last customer."); }
            if (solution2.Last != problem.Last) { throw new ArgumentException("Route and problem have to have the same last customer."); }

            var originalProblem = problem;
            var originalSolution1 = solution1;
            var originalSolution2 = solution2;
            if (!problem.Last.HasValue)
            { // convert to closed problem.
                Logger.Log("EAXOperator.Apply", Logging.TraceEventType.Warning,
                    string.Format("EAX operator cannot be applied to 'open' TSP's: converting problem and routes to a closed equivalent."));

                problem =  problem.ToClosed();
                solution1 = new Tour(solution1, 0);
                solution2 = new Tour(solution2, 0);
            }
            else if (problem.First != problem.Last)
            { // last is set but is not the same as first.
                Logger.Log("EAXSolver.Solve", Logging.TraceEventType.Warning,
                    string.Format("EAX operator cannot be applied to 'closed' TSP's with a fixed endpoint: converting problem and routes to a closed equivalent."));

                problem = problem.ToClosed();
                solution1 = new Tour(solution1, 0);
                solution2 = new Tour(solution2, 0);
                solution1.Remove(originalProblem.Last.Value);
                solution2.Remove(originalProblem.Last.Value);
            }
            fitness = float.MaxValue;
            var weights = problem.Weights;

            // first create E_a
            var e_a = new AsymmetricCycles(solution1.Count);
            foreach (var edge in solution1.Pairs())
            {
                e_a.AddEdge(edge.From, edge.To);
            }

            // create E_b
            var e_b = new int[solution2.Count];
            foreach (var edge in solution2.Pairs())
            {
                e_b[edge.To] = edge.From;
            }

            // create cycles.
            var cycles = new AsymmetricAlternatingCycles(solution2.Count);
            for (var idx = 0; idx < e_b.Length; idx++)
            {
                var a = e_a[idx];
                if (a != Constants.NOT_SET)
                {
                    var b = e_b[a];
                    if (idx != b && b != Constants.NOT_SET)
                    {
                        cycles.AddEdge(idx, a, b);
                    }
                }
            }

            // the cycles that can be selected.
            var selectableCycles = new List<int>(cycles.Cycles.Keys);

            int generated = 0;
            Tour best = null;
            while (generated < _maxOffspring
                && selectableCycles.Count > 0)
            {
                // select some random cycles.
                var cycleStarts = this.SelectCycles(selectableCycles);

                // copy if needed.
                AsymmetricCycles a = null;
                if (_maxOffspring > 1)
                {
                    a = e_a.Clone();
                }
                else
                {
                    a = e_a;
                }

                // take e_a and remove all edges that are in the selected cycles and replace them by the eges
                var nextArrayA = a.NextArray;
                foreach (int start in cycleStarts)
                {
                    var current = start;
                    var currentNext = cycles.Next(current);
                    do
                    {
                        a.AddEdge(currentNext.Value, currentNext.Key);

                        current = currentNext.Value;
                        currentNext = cycles.Next(current);
                    } while (current != start);
                }

                // connect all subtoures.
                var cycleCount = a.Cycles.Count;
                while (cycleCount > 1)
                {
                    // get the smallest tour.
                    var currentTour = new KeyValuePair<int, int>(-1, int.MaxValue);
                    foreach (KeyValuePair<int, int> tour in a.Cycles)
                    {
                        if (tour.Value < currentTour.Value)
                        {
                            currentTour = tour;
                        }
                    }

                    // first try nn approach.
                    var weight = double.MaxValue;
                    var selectedFrom1 = -1;
                    var selectedFrom2 = -1;
                    var selectedTo1 = -1;
                    var selectedTo2 = -1;

                    var ignoreList = new bool[a.Length];
                    int from;
                    int to;
                    from = currentTour.Key;
                    ignoreList[from] = true;
                    to = nextArrayA[from];
                    do
                    {
                        // step to the next ones.
                        from = to;
                        to = nextArrayA[from];

                        //ignore_list.Add(from);
                        ignoreList[from] = true;
                    } while (from != currentTour.Key);

                    if (_nn)
                    { // only try tours containing nn.

                        from = currentTour.Key;
                        to = nextArrayA[from];
                        var weightFromTo = weights[from][to];
                        do
                        {
                            // check the nearest neighbours of from
                            foreach (var nn in problem.GetNNearestNeighboursForward(10, from))
                            {
                                var nnTo = nextArrayA[nn];

                                if (nnTo != Constants.NOT_SET &&
                                    !ignoreList[nn] &&
                                    !ignoreList[nnTo])
                                {
                                    float mergeWeight =
                                        (weights[from][nnTo] + weights[nn][to]) -
                                        (weightFromTo + weights[nn][nnTo]);
                                    if (weight > mergeWeight)
                                    {
                                        weight = mergeWeight;
                                        selectedFrom1 = from;
                                        selectedFrom2 = nn;
                                        selectedTo1 = to;
                                        selectedTo2 = nnTo;
                                    }
                                }
                            }

                            // step to the next ones.
                            from = to;
                            to = nextArrayA[from];
                        } while (from != currentTour.Key);
                    }
                    if (selectedFrom2 < 0)
                    {
                        // check the nearest neighbours of from
                        foreach (var customer in solution1)
                        {
                            int customerTo = nextArrayA[customer];

                            if (!ignoreList[customer] &&
                                !ignoreList[customerTo])
                            {
                                var mergeWeight =
                                    (weights[from][customerTo] + weights[customer][to]) -
                                    (weights[from][to] + weights[customer][customerTo]);
                                if (weight > mergeWeight)
                                {
                                    weight = mergeWeight;
                                    selectedFrom1 = from;
                                    selectedFrom2 = customer;
                                    selectedTo1 = to;
                                    selectedTo2 = customerTo;
                                }
                            }
                        }
                    }

                    a.AddEdge(selectedFrom1, selectedTo2);
                    a.AddEdge(selectedFrom2, selectedTo1);

                    cycleCount--;
                }

                var newRoute = new Tour(new int[] { problem.First }, problem.Last);
                var previous = problem.First;
                var next = nextArrayA[problem.First];
                do
                {
                    newRoute.InsertAfter(previous, next);
                    previous = next;
                    next = nextArrayA[next];
                }
                while (next != Constants.NOT_SET &&
                    next != problem.First);

                var newFitness = 0.0f;
                foreach (var edge in newRoute.Pairs())
                {
                    newFitness = newFitness + weights[edge.From][edge.To];
                }

                if (newRoute.Count == solution1.Count)
                {
                    if (best == null ||
                        fitness > newFitness)
                    {
                        best = newRoute;
                        fitness = newFitness;
                    }

                    generated++;
                }
            }

            if (best == null)
            {
                best = new Tour(new int[] { problem.First }, problem.Last);
                var previous = problem.First;
                var next = e_a[problem.First];
                do
                {
                    best.InsertAfter(previous, next);
                    previous = next;
                    next = e_a[next];
                }
                while (next != Constants.NOT_SET &&
                    next != problem.First);

                fitness = 0.0f;
                foreach (var edge in best.Pairs())
                {
                    fitness = fitness + weights[edge.From][edge.To];
                }
            }

            if (!originalProblem.Last.HasValue)
            { // original problem as an 'open' problem, convert to an 'open' route.
                best = new Tour(best, null);
                fitness = objective.Calculate(problem, best);
            }
            else if (originalProblem.First != originalProblem.Last)
            { // original problem was a problem with a fixed last point different from the first point.
                best.InsertAfter(System.Linq.Enumerable.Last(best), originalProblem.Last.Value);
                best = new Tour(best, problem.Last.Value);
                fitness = objective.Calculate(originalProblem, best);
            }
            return best;
        }
    }
}

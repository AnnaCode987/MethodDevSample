/*
*  The ExcelFormula class processes excel formulas.
*/

import * as _ from "lodash";

import { parse } from 'excel-formula-parser';
import { Node, visit, CellNode } from 'excel-formula-ast';
import { ExcelFormulaVisitor } from './excelFormulaVisitor';

export interface ExcelFormulaResult {
  error: string;
  value: any;
}

export enum ExcelOperatorType {
  Unsupported,
  Math,
  Comparison
}

export const CELL_KEY_EXPRESSION = "^[c|C][1-9]+";

export class ExcelFormula {

  public columns: (number | string | boolean)[];
  public map: Map<Node, ExcelFormulaResult>;
  private visitor: ExcelFormulaVisitor;


  constructor() {
    this.visitor = new ExcelFormulaVisitor(this);
  }

  public evaluate(columns: (number | string | boolean)[], formula: string): ExcelFormulaResult {
    this.columns = columns;
    this.map = new Map<Node, ExcelFormulaResult>();

    const tree = parse(formula);

    visit(tree, this.visitor);

    return this.map.values().next().value;
  }

  public evalFunction(name: string, argNodes: Node[]): ExcelFormulaResult {

    let value;

    try {

      const args = this.getArgs(argNodes);

      switch (name) {
        case 'SUM':
          value = _.sum(this.filterByType(_.flatten(args), 'number'));
          return { error: null, value };

        case 'AVG':
          const avgArgs = this.filterByType(_.flatten(args), 'number') as number[];
          if (avgArgs.length < 1) {
            return { error: 'requires at least 1 argument', value: null };
          }

          value = _.reduce(avgArgs, (sum, n) => { return sum + n; }, 0) / avgArgs.length;
          return { error: null, value };

        case 'MOD':
          const modArgs = this.filterByType(_.flatten(args), 'number') as number[];
          if (modArgs.length > 2) {
            return { error: 'too many arguments or not numbers', value: null };
          }
          value = modArgs[0] % modArgs[1];
          return { error: null, value };

        case 'ABS':
          if (_.flatten(args).length > 1 && typeof (args[0]) !== 'number') {
            return { error: 'too many arguments or not a number', value: null };
          }
          value = Math.abs(args[0] as number);
          return { error: null, value };

        case 'MIN':
          value = this.filterByType(_.flatten(args), 'number');

          if (value.length < 1) {
            return { error: 'requires at least 1 argument', value: null };
          }
          value = _.min(value);
          return { error: null, value };

        case 'MAX':
          value = this.filterByType(_.flatten(args), 'number');

          if (value.length < 1) {
            return { error: 'requires at least 1 argument', value: null };
          }
          value = _.max(value);
          return { error: null, value };

        case 'COUNT':
          value = _.flatten(args).length; // TODO: ask Logan - do we filter out strings?
          return { error: null, value };

        case 'OR':
          const orArgs = _.flatten(args);
          if (orArgs.length < 1) {
            return { error: 'requires at least 1 argument', value: null };
          }

          value = orArgs.filter((a) => { if (a) { return true; } else { return false; } }).length !== 0;
          return { error: null, value };

        case 'AND':
          const andArgs = _.flatten(args)
          if (andArgs.length < 1) {
            return { error: 'requires at least 1 argument', value: null };
          }

          value = andArgs.filter((a) => { if (!a) { return true; } else { return false; } }).length === 0;
          return { error: null, value };

        case 'IF':
          const ifArgs = _.flatten(argNodes);
          if (ifArgs.length !== 3) {
            return { error: 'requires 3 arguments', value: null };
          }
          let result = this.map.get(ifArgs[0]);
          const resultValue1 = this.map.get(ifArgs[1]).value;
          const resultValue2 = this.map.get(ifArgs[2]).value;
          value = result.error === null && result.value ? resultValue1 : resultValue2;
          return { error: null, value };

        case 'IFERROR':
          const errArgs = _.flatten(argNodes);
          if (errArgs.length !== 2) {
            return { error: 'requires 2 arguments', value: null };
          }
          result = this.map.get(errArgs[0]);
          const resultValue = this.map.get(errArgs[1]).value;
          value = result.error !== null ? resultValue : result.value;
          return { error: null, value };

        default:
          return {
            error: 'not implemented',
            value: null
          };
      }
    }
    catch (err) {
      return {
        error: err,
        value: value
      }
    }
  }

  private filterByType(args: (number | string | boolean)[], by: string): (number | string | boolean)[] {
    return args.filter((a) => {
      return typeof (a) === by ? true : false;
    });
  }

  public evalBinary(operator: string, left: Node, right: Node): ExcelFormulaResult {

    let result = this.map.get(left);
    if (result.error != null) {
      return result;
    }
    const a = result.value;
    const atype = typeof (a);

    result = this.map.get(right);
    if (result.error != null) {
      return result;
    }
    const b = result.value;
    const btype = typeof (b);

    const math = (op: string, x: number, y: number) => {
      try {
        switch (op) {
          case '+':
            return x + y;
          case '-':
            return x - y;
          case '*':
            return x * y;
          case '/':
            return x / y;
          case '^':
            return x ** y;
          case '%':
            return x % y;
          default:
            break;
        }
      }
      catch (err) {
        return {
          error: err,
          value: null
        }
      }
    }

    if (atype === 'number' && btype === 'number') {
      const value = math(operator, a, b);
      if (value === Infinity || value === undefined || typeof value === 'number' && Number.isNaN(value)) {
        return {
          error: `'${value}'`,
          value: null
        }
      }
      return {
        error: null,
        value
      }
    }
    if (atype === 'string' && btype === 'string') {
      // are they dates?
    }

    return {
      error: 'not implemented',
      value: null
    };
  }

  public evalComparison(operator: string, left: Node, right: Node): ExcelFormulaResult {

    let result = this.map.get(left);
    if (result.error != null) {
      return result;
    }
    const a = result.value;
    const atype = typeof (a);

    result = this.map.get(right);
    if (result.error != null) {
      return result;
    }
    const b = result.value;
    const btype = typeof (b);

    const compare = (op: string, x: number, y: number) => {
      try {
        switch (op) {
          case '=':
            return x === y;
          case '<>':
            return x !== y;
          case '>':
            return x > y;
          case '<':
            return x < y;
          case '>=':
            return x >= y;
          case '<=':
            return x <= y;

          default:
            console.log('not implemented');
            break;
        }
      }
      catch (err) {
        return {
          error: err,
          value: null
        }
      }
    }


    if (atype === 'number' && btype === 'number') {
      const value = compare(operator, a, b);
      return {
        error: null,
        value
      }
    }

    return {
      error: 'not implemented',
      value: null
    };
  }

  public evalUnary(operator: string, operand: Node): ExcelFormulaResult {

    const math = (op: string, x: number) => {
      try {
        switch (op) {
          case '+':
            return +x;
          case '-':
            return -x;
          default:
            console.log('not implemented');
            break;
        }
      }
      catch (err) {
        return {
          error: err,
          value: null
        }
      }
    }

    let result = this.map.get(operand);
    if (result.error != null) {
      return result;
    }
    const a = result.value;
    const atype = typeof (a);

    if (atype === 'number') {
      let value = math(operator, a);

      return {
        error: null,
        value
      }
    }

  }

  public getColumnIndex(key: string): ExcelFormulaResult {

    if (key.match(CELL_KEY_EXPRESSION) === null) {
      return {
        error: 'invalid reference',
        value: null
      }
    }
    const index = parseInt(key.substring(1), 10) - 1;
    if (index < 0 && index >= this.columns.length) {
      return {
        error: 'column index out of range',
        value: null
      }
    }
    return {
      error: null,
      value: index
    }
  }

  public getColumnValue(key: string): ExcelFormulaResult {
    const result = this.getColumnIndex(key);
    if (result.error !== null) {
      return result;
    }
    return {
      error: null,
      value: this.columns[result.value]
    }
  }

  public getCellRangeValues(left: CellNode, right: CellNode): ExcelFormulaResult {
    const start = this.getColumnIndex(left.key);
    if (start.error !== null) {
      return start;
    }
    const end = this.getColumnIndex(right.key);
    if (end.error !== null) {
      return end;
    }

    const values = [];
    for (var i = start.value; i <= end.value; i++) {
      values.push(this.columns[i]);
    }

    return {
      error: null,
      value: values
    }
  }

  public getArgs(argsList: Node[]): (number | string | boolean)[] {
    const args = [];
    for (let i = 0; i < argsList.length; i++) {
      const result = this.map.get(argsList[i]);
      if (result.error !== null) {
        return [];
      }
      args.push(result.value);
    }
    return args;
  }

  public getOperatorType(operator: string): ExcelOperatorType {
    switch (operator) {
      case '+':
      case '-':
      case '*':
      case '/':
      case '^':
      case '%':
        return ExcelOperatorType.Math;

      case '/':
      case '=':
      case '<>':
      case '>':
      case '<':
      case '>=':
      case '<=':
        return ExcelOperatorType.Comparison;
      default:
        return ExcelOperatorType.Unsupported;
    }
  }
}

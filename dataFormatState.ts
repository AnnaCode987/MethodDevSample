/*
*  The DataFormat state for measures.
*/

"use strict";
import * as $ from "jquery";
import * as _ from "lodash";
import { cloneDeep } from "lodash.cloneDeep";

import powerbi from "powerbi-visuals-api";
import { grid } from "./grid";
import { ColumnGroupConverter } from "./columnGroupConverter";
import { MetaMeasure } from "./dataConverter";
import { ColumnDataType } from "./dataTypes";
import VisualUpdateOptions = powerbi.extensibility.visual.VisualUpdateOptions;
import IVisualHost = powerbi.extensibility.visual.IVisualHost;
import VisualObjectInstance = powerbi.VisualObjectInstance;
import VisualObjectInstancesToPersist = powerbi.VisualObjectInstancesToPersist;

export enum DataMagnitude {
    Actual = 0,
    Thousands = 1,
    Millions = 2,
    Billions = 3,
    Trillions = 4
}

export interface DataFormat {
    measure: string,  // queryName from MetaMeasure
    type: ColumnDataType,
    measureCaption?: string,
    magnitude: DataMagnitude,
    precision?: number,
    format?: string,
}

export interface DataFormatProps {
    dataFormats: DataFormat[];
}

// Tested with these formats
// 0.000 %;-0.000 %;0.000000000 %
// \$#,0.00;(\$#,0.00);\$#,0.00
// #,0
// #0.0
const PRECISION_EXPRESSION: string = "0\\.?0*";    // "\\.[0-9]+"   original numeric
export const PRECISION_TEMPLATE: string = "0.@@PRE@@MAG@@"; //.@@PRE@@MAG@@   numeric 

export class DataFormatState {

    private initialProps: DataFormatProps;
    private props: DataFormatProps;

    private stateObj: powerbi.DataViewObject;

    constructor(private visualHost: IVisualHost,
        private grid: grid) {
        this.props = this.getDefaultState();
    }

    public get formatProps(): DataFormatProps { return this.props };

    private getDefaultState(): DataFormatProps {
        this.props = { dataFormats: [] };

        if (!this.grid.loader.measures) {
            return this.props;
        }

        this.grid.loader.measures.forEach(m => {
            const [dataType, format] = ColumnGroupConverter.convertDataType(m.type);

            this.props.dataFormats.push({
                measure: m.queryName,
                type: dataType,
                magnitude: DataMagnitude.Actual,
                precision: this.getPrecision(m),
                format: DataFormatState.getFormatTemplate(ColumnGroupConverter.getFormat(m, format)),
            });
        });

        return this.props;
    }

    public static getFormatTemplate(f: string): string {
        let format = f;

        if (format !== undefined && format !== null && format.length !== 0) {
            // Decimal, percentages or integer "#,0.00;(#,0.00);#,0.00" 
            return format.replace(new RegExp(PRECISION_EXPRESSION, "g"), `${PRECISION_TEMPLATE}`);
            // return "#,0.@@PRE@@MAG@@;(#,0.@@PRE@@MAG@@);#,0.@@PRE@@MAG@@"
        }

        return format;
    }

    private getPrecision(m: MetaMeasure): number | null {
        if (!m.type.numeric) {
            return null;
        }

        if (m.type.integer) {
            return 0;
        }

        if (m.format === undefined || m.format === null || m.format.length === 0) {
            return 2;
        }

        const matches = m.format.match(PRECISION_EXPRESSION);
        return matches ? matches[0].length - 2 : 2;
    }

    public static applyDataFormat(df: DataFormat): string {
        let mag = "";
        let zeros = "0".repeat(df.precision);

        switch (df.magnitude) {
            case DataMagnitude.Thousands:
                mag = "K"
                break;
            case DataMagnitude.Millions:
                mag = "M";
                break;
            case DataMagnitude.Billions:
                mag = "B";
                break;
            case DataMagnitude.Trillions:
                mag = "T";
                break;
            default:
                break;
        }
        return df.format.replace(new RegExp(PRECISION_TEMPLATE, "g"), `0.${zeros}${mag}`);
    }

    private hasMeasuresChanged(): boolean {
        if (this.grid.loader.measures.length !== this.initialProps.dataFormats.length) {
            return true;
        }

        const df = this.initialProps.dataFormats;
        this.grid.loader.measures.forEach(p => {
            const found = df.find(x => x.measure === p.queryName);
            if (!found) {
                return true;
            }
        });
        return false;
    }

    private updateProps(dataFormats: DataFormat[], props: DataFormatProps,) {

        dataFormats.forEach(p => {
            const found = props.dataFormats.find(x => x.measure === p.measure);
            if (found) {
                found.magnitude = p.magnitude;
                found.precision = p.precision;
            }
        })
    }

    public getCustomFormat(m: MetaMeasure): DataFormat {

        var df = this.initialProps.dataFormats.find(x => x.measure === m.queryName);

        return (df.magnitude !== DataMagnitude.Actual || df.precision !== this.getPrecision(m)) ? df : null;
    }

    /** methods used in preserving state */

    public init(options: VisualUpdateOptions): boolean {
        if (this.initialProps && this.stateObj || (this.initialProps && this.grid.loader.measures)) {

            if (this.hasMeasuresChanged()) {

                const prev = this.clone(this.props);
                this.initialProps = this.getDefaultState();

                this.updateProps(prev.dataFormats, this.initialProps);
                this.updateProps(prev.dataFormats, this.props);
            }
            return false;
        }

        const objects = options.dataViews[0].metadata.objects;
        this.stateObj = objects ? objects.dataFormatState : null;

        if (!this.stateObj) {
            if (this.initialProps) {
                return false;
            }
            this.initialProps = this.getDefaultState();

        } else {
            this.initialProps = this.jsonToDataFormatState(this.stateObj.dataFormatsJson.toString())
        }

        this.props = this.clone(this.initialProps);
        return true;
    }

    public setDataProps(props: DataFormatProps): boolean {
        if (!this.hasStateChanged(props)) {
            return false;
        }

        this.props = props;
        this.persist();
        return true;
    }

    public persist() {
        const props = this.props;

        this.initialProps = this.clone(props);

        const object: VisualObjectInstance[] = [{
            objectName: "dataFormatState",
            selector: null,
            properties: {
                dataFormatsJson: this.stringifyDataFormats()
            }
        }];

        const propertyToChange: VisualObjectInstancesToPersist = {
            replace: object
        }

        this.visualHost.persistProperties(propertyToChange);
    }

    private clone(props: DataFormatProps): DataFormatProps {
        return _.cloneDeep(props);
    }

    private jsonToDataFormatState(jsonStr: string): DataFormatProps | null {
        if (!jsonStr) {
            return null;
        }

        const dataFormats: DataFormat[] = JSON.parse(jsonStr);
        const props = this.getDefaultState();

        this.updateProps(dataFormats, props);
        return props;
    }

    private stringifyDataFormats(): string {
        const props: DataFormatProps = { dataFormats: [] };

        this.props.dataFormats.forEach(p => {
            if (p.magnitude !== DataMagnitude.Actual || p.precision !== 2) {
                props.dataFormats.push(
                    {
                        measure: p.measure,
                        type: p.type,
                        magnitude: p.magnitude,
                        precision: p.precision
                    }
                );
            }
        });

        return props.dataFormats && props.dataFormats.length != 0 ?
            JSON.stringify(props.dataFormats) : null;
    }

    private hasStateChanged(newState: DataFormatProps): boolean {
        return this.initialProps.dataFormats.some(d => newState.dataFormats.find(n => n.measure === d.measure && (d.magnitude !== n.magnitude || d.precision !== n.precision)));
    }
}

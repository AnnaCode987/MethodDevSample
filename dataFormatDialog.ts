/*
*  The DataFormat class manages the DataFormat state.
*/

"use strict";
import * as $ from "jquery";
import * as _ from "lodash";

import { DropDownList, SelectEventArgs } from '@syncfusion/ej2-dropdowns';
import { NumericTextBox, ChangeEventArgs, FormValidator, FormValidatorModel, ValidArgs } from '@syncfusion/ej2-inputs';
import { DataFormatProps, DataMagnitude } from "./dataFormatState";
import { Dialog } from '@syncfusion/ej2-popups';
import { enableRipple } from '@syncfusion/ej2-base';

type OkCallback = (n: DataFormatProps) => void;
type CancelCallback = () => void;

enableRipple(true);
export class DataFormatDialog {
    private el: JQuery;
    private elDialogBox: JQuery;
    private precisionBox: NumericTextBox;
    private measuresDropDown: DropDownList;
    private magnitudeDropDown: DropDownList;
    private dataFormatDialog: Dialog;
    private validationOptions: FormValidatorModel;
    private validator: FormValidator;
    private elementAppended: boolean = false;
    private workingProps: DataFormatProps;
    private formatProps: DataFormatProps;

    constructor(private okCallback: OkCallback,
        private cancelCallback: CancelCallback,
        target: HTMLElement) {

        this.initEl();
        this.initMagnitude();
        this.elDialogBox = $("<span class='dialogBox'></span>");
        $(target).append(this.elDialogBox);

        var width = $(target).width();
        var height = $(target).height();

        this.dataFormatDialog = new Dialog({
            header: "<div>Format Data</div>",
            buttons: [
                {
                    'click': () => { this.Save(); },
                    buttonModel: {
                        isPrimary: true,
                        content: 'OK',
                    },
                    type: 'Submit'
                },
                {
                    'click': () => { this.closeDialog(); },
                    buttonModel: {
                        content: 'Cancel',
                    },
                    type: 'Reset'
                }
            ],
            isModal: true,
            enableResize: false,
            allowDragging: true,
            closeOnEscape: true,
            content: this.el[0],
            target,
            position: { X: 'center', Y: 'center' },
            width: `${Math.min(width, 400)}px`,
            minHeight: '350px',
        });
    }

    private initEl(): JQuery {
        this.el = $(`
            <div>
                <form id="dataform">
                    <div>
                        <div class="dropdown-header">Select Data</div>
                        <input name="measuresDD">
                    </div>
                    <br/>
                    <div>
                        <div class="dropdown-header">Display Units</div>
                        <input name="magnitudeDD">
                    </div>
                    <br/>
                    <div>
                        <div class="dropdown-header">Decimals Places</div>
                        <input name="precisionBox">
                    </div>
                </form>
            <div>`);


        this.precisionBox = new NumericTextBox({
            format: 'n',
            value: 0,
            min: 0,
            max: 10,
            step: 1,
            change: this.precisionChanged.bind(this)
        });

        this.precisionBox.appendTo(this.el.find('input[name="precisionBox"]')[0]);

        this.measuresDropDown = new DropDownList({
            fields: { text: 'value', value: 'id' },
            popupWidth: "350px",
            popupHeight: "250px",

            select: this.measuresChanged.bind(this)
        });
        this.measuresDropDown.appendTo(this.el.find('input[name="measuresDD"]')[0]);

        return this.el;
    }

    private initMeasures() {
        let ds: { [key: string]: Object }[] = [];

        this.formatProps.dataFormats.forEach(d => {
            ds.push({
                id: d.measure,
                value: d.measure
            });
        });

        this.measuresDropDown.dataSource = ds;
        this.measuresDropDown.dataBind();
    }

    private initMagnitude() {
        let ds: { [key: string]: Object }[] = [];

        for (var m in DataMagnitude) {
            if (isNaN(Number(m))) {
                ds.push({
                    id: DataMagnitude[m],
                    value: m
                });
            }
        }

        this.magnitudeDropDown = new DropDownList({
            dataSource: ds,
            fields: { text: 'value', value: 'id' },
            popupWidth: "350px",
            popupHeight: "250px",
            select: this.magnitudeChanged.bind(this)
        });

        this.magnitudeDropDown.appendTo(this.el.find('input[name="magnitudeDD"]')[0]);
    }

    private measuresChanged(e: SelectEventArgs) {
        this.validator.reset();

        const df = this.workingProps.dataFormats.find(d => d.measure === e.item.id);
        if (df) {
            this.magnitudeDropDown.value = df.magnitude;
            this.precisionBox.value = df.precision;
        }
    }

    private magnitudeChanged(e: SelectEventArgs) {
        const df = this.workingProps.dataFormats.find(d => d.measure === this.measuresDropDown.value);
        if (df) {
            df.magnitude = parseInt(e.item.id);
        }
    }

    private precisionChanged(e: ChangeEventArgs) {
        const df = this.workingProps.dataFormats.find(d => d.measure === this.measuresDropDown.value);

        if (df) {
            df.precision = e.value;
        }
    }

    private Save() {
        var valid = this.validator.validate();

        if (valid) {
            this.okCallback(this.workingProps);
            this.dataFormatDialog.hide();
        }
    }


    public closeDialog(): void {
        this.validator.reset();
        this.cancelCallback();
    }

    public hide() {
        this.dataFormatDialog.hide();
    }

    public open(props: DataFormatProps): void {
        if (!this.elementAppended) {
            this.dataFormatDialog.appendTo(this.elDialogBox[0]);
            this.elementAppended = !this.elementAppended;
        }

        this.formatProps = props;
        this.workingProps = _.cloneDeep(props);
        this.initMeasures();
        this.magnitudeDropDown.value = null;
        this.measuresDropDown.value = null;
        this.precisionBox.value = 0;

        this.validationOptions = {
            rules: {
                'measuresDD': { required: [true, 'Please select a measure.'] }
            },
            customPlacement: (inputElement, errorElement) => {
                inputElement.parentElement.parentElement.appendChild(errorElement);
            }
        };

        this.validator = new FormValidator('#dataform', this.validationOptions);

        // minHeight was removed by Syncfusion's height sizing algorithmn and re-added here.
        this.dataFormatDialog.minHeight = '350px';
        this.dataFormatDialog.show();
    }
}
